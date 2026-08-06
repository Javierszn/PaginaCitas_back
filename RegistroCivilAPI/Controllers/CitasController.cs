using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authorization;
using RegistroCivilAPI.Models;

namespace RegistroCivilAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CitasController : ControllerBase
    {
        private readonly RegistroCivilCitasContext _context;
        private readonly IConfiguration _config;

        public CitasController(RegistroCivilCitasContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        private async Task AutoActualizarInasistenciasAsync()
        {
            await _context.Database.ExecuteSqlRawAsync(
                "UPDATE Citas SET estatus = 'NO_ASISTIO' WHERE estatus IN ('PROGRAMADA', 'CONFIRMADA', 'REPROGRAMADA') AND fecha_hora_fin < GETDATE()"
            );
        }

        [HttpGet("Horarios")]
        public async Task<ActionResult<IEnumerable<string>>> ObtenerHorariosDisponibles(int idSede, int idTramite, DateTime fecha)
        {
            if (fecha.Date < DateTime.Today) return Ok(new List<string>());

            var config = await _context.ConfiguracionAgendas.FirstOrDefaultAsync(c => c.Id == 1);
            if (config != null)
            {
                if (fecha.Date < config.FechaInicio.Date || fecha.Date > config.FechaFin.Date)
                    return Ok(new List<string>());
            }

            var inhabil = await _context.DiasInhabiles.AnyAsync(d => d.FechaBloqueada == DateOnly.FromDateTime(fecha) && (d.IdSede == idSede || d.IdSede == null));
            if (inhabil) return Ok(new List<string>());

            byte diaSemana = (byte)(fecha.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)fecha.DayOfWeek);
            var horarioSede = await _context.HorariosSedes.FirstOrDefaultAsync(h => h.IdSede == idSede && h.DiaSemana == diaSemana);
            if (horarioSede == null) return Ok(new List<string>());
            var tramite = await _context.Tramites.FindAsync(idTramite);
            if (tramite == null) return BadRequest("Trámite no encontrado");

            if (tramite.FechaInicioPermitida.HasValue && fecha.Date < tramite.FechaInicioPermitida.Value.Date)
                return Ok(new List<string>());

            if (tramite.FechaFinPermitida.HasValue && fecha.Date > tramite.FechaFinPermitida.Value.Date)
                return Ok(new List<string>());

            int intervalo = tramite.DuracionMinutos > 0 ? tramite.DuracionMinutos : 30;
            int limiteDiario = tramite.LimiteDiarioSede > 0 ? tramite.LimiteDiarioSede : 999;

            var cantidadCitasDia = await _context.Citas
                .CountAsync(c => c.IdSede == idSede && c.IdTramite == idTramite && c.FechaHoraInicio.Date == fecha.Date && (c.Estatus == "PROGRAMADA" || c.Estatus == "REPROGRAMADA"));

            if (cantidadCitasDia >= limiteDiario) return Ok(new List<string>());

            var horasOcupadas = await _context.Citas
                .Where(c => c.IdSede == idSede && c.FechaHoraInicio.Date == fecha.Date && (c.Estatus == "PROGRAMADA" || c.Estatus == "REPROGRAMADA"))
                .Select(c => TimeOnly.FromDateTime(c.FechaHoraInicio)).ToListAsync();

            var horasDisponibles = new List<string>();
            TimeOnly horaActual = horarioSede.HoraApertura;
            TimeOnly now = TimeOnly.FromDateTime(DateTime.Now);

            while (horaActual < horarioSede.HoraCierre)
            {
                if (fecha.Date == DateTime.Today && horaActual <= now) { horaActual = horaActual.AddMinutes(intervalo); continue; }
                if (!horasOcupadas.Contains(horaActual)) { horasDisponibles.Add(horaActual.ToString("HH:mm")); }
                horaActual = horaActual.AddMinutes(intervalo);
            }
            return Ok(horasDisponibles);
        }

        [HttpPost]
        public async Task<ActionResult> AgendarCita([FromBody] CitaDTO solicitud)
        {
            if (!await ValidarReCaptcha(solicitud.CaptchaToken))
                return BadRequest(new { mensaje = "Verificación de seguridad fallida. Por favor, complete el Captcha." });
            try
            {
                Ciudadano ciudadano = null;
                string curpBuscado = string.IsNullOrWhiteSpace(solicitud.Curp) ? null : solicitud.Curp.Trim().ToUpper();
                string nombreBuscado = string.IsNullOrWhiteSpace(solicitud.Nombre) ? null : solicitud.Nombre.Trim().ToUpper();
                string telLimpio = solicitud.Telefono?.Trim();


                if (!string.IsNullOrWhiteSpace(nombreBuscado))
                {
                    var usuarioConMismoTel = await _context.Ciudadanos.FirstOrDefaultAsync(c => c.Telefono == telLimpio);
                    if (usuarioConMismoTel != null && usuarioConMismoTel.Nombre.ToUpper() != nombreBuscado)
                    {
                        return BadRequest(new { mensaje = "Alerta de Seguridad: Este número de teléfono ya está registrado a nombre de otra persona. No se permiten gestores." });
                    }
                }


                if (!string.IsNullOrWhiteSpace(curpBuscado))
                {
                    ciudadano = await _context.Ciudadanos.FirstOrDefaultAsync(c => c.Curp == curpBuscado);
                }

                if (ciudadano == null && !string.IsNullOrWhiteSpace(nombreBuscado))
                {
                    ciudadano = await _context.Ciudadanos.FirstOrDefaultAsync(c => c.Nombre.ToUpper() == nombreBuscado && c.Telefono == telLimpio);
                }

                if (ciudadano == null)
                {
                    string curpFinal = curpBuscado ?? "ENM" + Guid.NewGuid().ToString("N").Substring(0, 15).ToUpper();
                    string nombreSeguro = string.IsNullOrWhiteSpace(solicitud.Nombre) ? "" : solicitud.Nombre.Trim();
                    var partesNombre = nombreSeguro.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    string origenRegistro = string.IsNullOrWhiteSpace(solicitud.EstadoRegistro) ? solicitud.MunicipioRegistro : $"{solicitud.EstadoRegistro} - {solicitud.MunicipioRegistro}";
                    if (string.IsNullOrWhiteSpace(origenRegistro)) origenRegistro = "MANUAL";
                    if (origenRegistro.Length > 145) origenRegistro = origenRegistro.Substring(0, 145);

                    ciudadano = new Ciudadano
                    {
                        Curp = curpFinal,
                        Nombre = partesNombre.Length > 0 ? partesNombre[0].Substring(0, Math.Min(partesNombre[0].Length, 50)) : "",
                        PrimerApellido = partesNombre.Length > 1 ? partesNombre[1].Substring(0, Math.Min(partesNombre[1].Length, 50)) : "",
                        SegundoApellido = partesNombre.Length > 2 ? string.Join(" ", partesNombre.Skip(2)).Substring(0, Math.Min(string.Join(" ", partesNombre.Skip(2)).Length, 50)) : "",
                        Correo = solicitud.Correo?.Trim().Substring(0, Math.Min(solicitud.Correo.Trim().Length, 100)),
                        Telefono = telLimpio.Substring(0, Math.Min(telLimpio.Length, 15)),
                        OrigenRegistro = origenRegistro
                    };
                    _context.Ciudadanos.Add(ciudadano);
                    await _context.SaveChangesAsync();
                }


                var penalizado = await _context.Citas.AnyAsync(c => c.IdCiudadano == ciudadano.IdCiudadano && c.Estatus == "NO_ASISTIO" && c.FechaHoraInicio >= DateTime.Now.AddDays(-7));
                if (penalizado)
                {
                    return BadRequest(new { mensaje = "Sistema de Penalización: Usted cuenta con una inasistencia reciente. Por reglamento, podrá agendar nuevas citas al transcurrir 1 semana desde la falta." });
                }


                var citaMismoTramite = await _context.Citas.AnyAsync(c =>
                    c.IdCiudadano == ciudadano.IdCiudadano &&
                    c.IdTramite == solicitud.IdTramite &&
                    c.FechaHoraInicio.Date == solicitud.FechaHora.Date &&
                    (c.Estatus == "PROGRAMADA" || c.Estatus == "REPROGRAMADA"));

                if (citaMismoTramite) return BadRequest(new { mensaje = "Alerta: Usted ya tiene una cita programada para este trámite exacto en este día." });

                string folio = Guid.NewGuid().ToString().Substring(0, 8);
                string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Desconocida";

                await _context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Citas (id_cita, id_ciudadano, id_tramite, id_sede, fecha_hora_inicio, fecha_hora_fin, estatus, ip_origen, navegador, sistema_operativo) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, 'PROGRAMADA', {6}, {7}, {8})",
                    folio, ciudadano.IdCiudadano, solicitud.IdTramite, solicitud.IdSede, solicitud.FechaHora, solicitud.FechaHora.AddMinutes(30), ip, solicitud.Navegador ?? "Desconocido", solicitud.SistemaOperativo ?? "Desconocido");

                var tramiteEntity = await _context.Tramites.FindAsync(solicitud.IdTramite);
                var sedeEntity = await _context.Sedes.FindAsync(solicitud.IdSede);
                string nombreSede = sedeEntity?.Nombre ?? "Oficina del Registro Civil";
                string requisitosTramite = tramiteEntity?.Requisitos ?? "Por favor comuníquese a la sede para confirmar los requisitos obligatorios.";

                string nombreCompleto = $"{ciudadano.Nombre} {ciudadano.PrimerApellido} {ciudadano.SegundoApellido}".Trim();
                string identificadorParaCorreo = !string.IsNullOrWhiteSpace(nombreCompleto) ? nombreCompleto : $"CURP: {ciudadano.Curp}";

                await EnviarCorreoConfirmacion(ciudadano.Correo, identificadorParaCorreo, folio, solicitud.FechaHora, tramiteEntity?.NombreTramite ?? "Trámite General", tramiteEntity?.Costo ?? 0, nombreSede, requisitosTramite);

                return Ok(new { mensaje = "Cita agendada con éxito", folio = folio });
            }
            catch (Exception ex)
            {
                var errorReal = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return StatusCode(500, new { mensaje = "Error de base de datos", detalle = errorReal });
            }
        }


        private async Task EnviarCorreoConfirmacion(string correoDestino, string identificador, string folio, DateTime fechaHora, string tramite, decimal costo, string sede, string requisitos, bool esReagendada = false)
        {
            try
            {
                string correoOrigen = _config["EmailSettings:Correo"];
                string passwordApp = _config["EmailSettings:PasswordApp"];

                if (string.IsNullOrEmpty(correoOrigen) || string.IsNullOrEmpty(passwordApp)) return;

                var smtpClient = new SmtpClient("smtp.gmail.com")
                {
                    Port = 587,
                    Credentials = new NetworkCredential(correoOrigen, passwordApp),
                    EnableSsl = true,
                };

                string listaRequisitosHtml = "";
                if (!string.IsNullOrWhiteSpace(requisitos))
                {
                    var lineas = requisitos.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var linea in lineas) { listaRequisitosHtml += $"<li style='margin-bottom: 8px;'>{linea.Trim('•', ' ', '-')}</li>"; }
                }


                string tituloPrincipal = esReagendada ? "Confirmación de Cita Reagendada" : "Confirmación de Cita Registrada";
                string textoSecundario = esReagendada ? "Su cita ha sido reagendada exitosamente para una nueva fecha." : "Su cita ha sido generada exitosamente.";

                var mensajeHtml = $@"
                <div style='font-family: Arial, sans-serif; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #ddd; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 8px rgba(0,0,0,0.1);'>
                    <div style='text-align: center; background-color: #ffffff; padding: 0;'>
                        <img src='http://201.144.103.221/citas/images/Sin_titulo.png' alt='Gobierno del Estado SLP' style='width: 100%; height: auto;' />
                    </div>
                    <div style='padding: 30px 20px;'>
                        <h2 style='color: #055A1C; text-align: center; margin-top: 0;'>{tituloPrincipal}</h2>
                        <p style='font-size: 15px; margin-top: 20px;'>Estimado/a <b>{identificador}</b>,</p>
                        <p style='font-size: 15px;'>{textoSecundario} A continuación, le presentamos los detalles:</p>
                        
                        <div style='background-color: #f9f9f9; padding: 20px; border-radius: 6px; border-left: 5px solid #055A1C; margin: 25px 0;'>
                            <p style='margin: 0 0 10px 0; font-size: 15px;'><b>Trámite:</b> {tramite}</p>
                            <p style='margin: 0 0 10px 0; font-size: 15px;'><b>Costo del Servicio:</b> <span style='color: #055A1C; font-weight: bold;'>${costo.ToString("0.00")}</span></p>
                            <p style='margin: 0 0 10px 0; font-size: 15px;'><b>Nueva Fecha y Hora:</b> <span style='color: #E60064; font-weight: bold;'>{fechaHora.ToString("dd/MM/yyyy HH:mm")} hrs</span></p>
                            <p style='margin: 0 0 15px 0; font-size: 15px;'><b>Sede:</b> {sede}</p>
                            <h3 style='margin: 0; color: #055A1C; font-size: 20px;'>FOLIO: {folio}</h3>
                        </div>

                        <h4 style='color: #055A1C; margin-top: 30px; margin-bottom: 10px; font-size: 16px;'>📋 REQUISITOS OBLIGATORIOS</h4>
                        <div style='background-color: #fff9e6; padding: 15px 20px; border: 1px dashed #ffc107; border-radius: 6px;'>
                            <ul style='color: #555; line-height: 1.5; font-size: 14px; margin: 0; padding-left: 20px;'>
                                {listaRequisitosHtml}
                            </ul>
                        </div>

                        <h4 style='color: #E60064; margin-top: 30px; margin-bottom: 10px; font-size: 16px;'>⚠️ AVISOS IMPORTANTES Y PENALIZACIÓN</h4>
                        <ul style='color: #555; line-height: 1.6; padding-left: 20px; font-size: 14px; margin-top: 0;'>
                            <li><strong>El trámite es estrictamente personal.</strong> Es obligatorio presentar Identificación Oficial (ID) vigente.</li>
                            <li><strong>SISTEMA DE PENALIZACIÓN:</strong> Si usted agenda su cita y NO asiste, el sistema lo bloqueará automáticamente, impidiéndole agendar un nuevo trámite durante <strong>1 semana</strong>.</li>
                        </ul>

                        <hr style='border: 0; border-top: 1px solid #eee; margin: 30px 0;' />
                        <p style='font-size: 11px; color: #999; text-align: center; margin: 0;'>Por favor, <strong>NO conteste este correo.</strong> Las respuestas a esta dirección no son monitoreadas.</p>
                    </div>
                </div>";

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(correoOrigen, "Registro Civil Citas"),
                    Subject = $"{tituloPrincipal} - Folio: {folio}",
                    Body = mensajeHtml,
                    IsBodyHtml = true,
                };
                mailMessage.To.Add(correoDestino);
                await smtpClient.SendMailAsync(mailMessage);
            }
            catch (System.Exception ex) { System.Console.WriteLine("ERROR AL ENVIAR CORREO: " + ex.Message); }
        }

        [HttpGet("{folio}")]
        public async Task<ActionResult> ObtenerCita(string folio, [FromQuery] string captchaToken)
        {
            if (!await ValidarReCaptcha(captchaToken))
                return BadRequest(new { mensaje = "Verificación de seguridad fallida. Complete el Captcha." });

            await AutoActualizarInasistenciasAsync();

            var cita = await _context.Citas
                .Include(c => c.IdCiudadanoNavigation)
                .Include(c => c.IdTramiteNavigation)
                .Include(c => c.IdSedeNavigation)
                .FirstOrDefaultAsync(c => c.IdCita == folio);

            if (cita == null) return NotFound(new { mensaje = "No se encontró ninguna cita registrada con este folio." });

            return Ok(new
            {
                folio = cita.IdCita,
                estatus = cita.Estatus,
                fecha = cita.FechaHoraInicio.ToString("yyyy-MM-dd"),
                hora = cita.FechaHoraInicio.ToString("HH:mm"),
                idTramite = cita.IdTramite,
                tramite = cita.IdTramiteNavigation.NombreTramite,
                requisitos = cita.IdTramiteNavigation.Requisitos,
                costo = cita.IdTramiteNavigation.Costo,
                duracion = cita.IdTramiteNavigation.DuracionMinutos,
                idSede = cita.IdSede,
                sede = cita.IdSedeNavigation.Nombre,
                direccion = cita.IdSedeNavigation.Direccion,
                ciudadano = $"{cita.IdCiudadanoNavigation.Nombre} {cita.IdCiudadanoNavigation.PrimerApellido} {cita.IdCiudadanoNavigation.SegundoApellido}".Trim(),
                curp = cita.IdCiudadanoNavigation.Curp
            });
        }

        [HttpPut("{folio}/cancelar")]
        public async Task<ActionResult> CancelarCita(string folio)
        {
            var cita = await _context.Citas.FirstOrDefaultAsync(c => c.IdCita == folio);
            if (cita == null) return NotFound(new { mensaje = "Cita no encontrada." });
            if (cita.Estatus == "CANCELADA") return BadRequest(new { mensaje = "La cita ya se encuentra cancelada." });
            if (cita.FechaHoraInicio < DateTime.Now) return BadRequest(new { mensaje = "No se puede cancelar una cita de una fecha que ya pasó." });

            cita.Estatus = "CANCELADA"; await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Su cita ha sido cancelada con éxito. El espacio ha sido liberado." });
        }

        [HttpPut("{folio}/reagendar")]
        public async Task<ActionResult> ReagendarCita(string folio, [FromBody] ReagendarDTO dto)
        {
            var cita = await _context.Citas
                .Include(c => c.IdCiudadanoNavigation)
                .Include(c => c.IdTramiteNavigation)
                .Include(c => c.IdSedeNavigation)
                .FirstOrDefaultAsync(c => c.IdCita == folio);

            if (cita == null) return NotFound(new { mensaje = "Cita no encontrada." });
            if (cita.Estatus == "CANCELADA" || cita.Estatus == "ATENDIDA" || cita.Estatus == "NO_ASISTIO" || cita.Estatus == "REPROGRAMADA")
                return BadRequest(new { mensaje = "Solo se permite reprogramar la cita una vez. El estatus actual es " + cita.Estatus });

            cita.FechaHoraInicio = dto.NuevaFechaHora;
            cita.FechaHoraFin = dto.NuevaFechaHora.AddMinutes(30);
            cita.Estatus = "REPROGRAMADA";
            await _context.SaveChangesAsync();

            string nombreCompleto = $"{cita.IdCiudadanoNavigation.Nombre} {cita.IdCiudadanoNavigation.PrimerApellido} {cita.IdCiudadanoNavigation.SegundoApellido}".Trim();
            string identificadorParaCorreo = !string.IsNullOrWhiteSpace(nombreCompleto) ? nombreCompleto : $"CURP: {cita.IdCiudadanoNavigation.Curp}";

            await EnviarCorreoConfirmacion(cita.IdCiudadanoNavigation.Correo, identificadorParaCorreo, folio, dto.NuevaFechaHora, cita.IdTramiteNavigation.NombreTramite, cita.IdTramiteNavigation.Costo, cita.IdSedeNavigation.Nombre, cita.IdTramiteNavigation.Requisitos, true);

            return Ok(new { mensaje = "Cita reagendada con éxito." });
        }

        // --- RUTAS ADMINISTRATIVAS (AHORA PROTEGIDAS) ---

        [HttpGet("PorSede/{idSede}")]
        [Authorize] // <--- Seguridad añadida
        public async Task<ActionResult> ObtenerCitasPorSede(int idSede, [FromQuery] string? fecha = null, [FromQuery] string? busqueda = null)
        {
            await AutoActualizarInasistenciasAsync();
            var query = _context.Citas.Include(c => c.IdCiudadanoNavigation).Include(c => c.IdTramiteNavigation).Where(c => c.IdSede == idSede).AsQueryable();

            if (!string.IsNullOrWhiteSpace(busqueda))
            {
                busqueda = busqueda.ToLower();
                query = query.Where(c => c.IdCita.ToLower().Contains(busqueda) || c.IdCiudadanoNavigation.Curp.ToLower().Contains(busqueda) || c.IdCiudadanoNavigation.Nombre.ToLower().Contains(busqueda) || c.IdCiudadanoNavigation.PrimerApellido.ToLower().Contains(busqueda));
            }
            else
            {
                DateTime fechaFiltro = DateTime.Today;
                if (!string.IsNullOrEmpty(fecha) && DateTime.TryParse(fecha, out DateTime parsedDate)) fechaFiltro = parsedDate.Date;
                query = query.Where(c => c.FechaHoraInicio.Year == fechaFiltro.Year && c.FechaHoraInicio.Month == fechaFiltro.Month && c.FechaHoraInicio.Day == fechaFiltro.Day);
            }

            var citas = await query.OrderBy(c => c.FechaHoraInicio).Select(c => new {
                folio = c.IdCita,
                ciudadano = $"{c.IdCiudadanoNavigation.Nombre} {c.IdCiudadanoNavigation.PrimerApellido} {c.IdCiudadanoNavigation.SegundoApellido}".Trim(),
                curp = c.IdCiudadanoNavigation.Curp,
                tramite = c.IdTramiteNavigation.NombreTramite,
                fechaStr = c.FechaHoraInicio.ToString("dd/MM/yyyy"),
                hora = c.FechaHoraInicio.ToString("HH:mm"),
                estatus = c.Estatus,
                ip = c.IpOrigen,
                navegador = c.Navegador,
                so = c.SistemaOperativo
            }).ToListAsync();
            return Ok(citas);
        }

        [HttpPut("{folio}/actualizarEstatus")]
        [Authorize] // <--- Seguridad añadida
        public async Task<ActionResult> ActualizarEstatus(string folio, [FromBody] CambioEstatusDTO dto)
        {
            var cita = await _context.Citas.FirstOrDefaultAsync(c => c.IdCita == folio);
            if (cita == null) return NotFound(new { mensaje = "Cita no encontrada." });

            string valorAnterior = cita.Estatus;
            cita.Estatus = dto.NuevoEstatus;

            var bitacora = new BitacoraAuditorium { IdUsuarioInterno = dto.IdUsuarioInterno, TablaAfectada = "Citas", AccionRealizada = "UPDATE", RegistroId = folio, ValorAnterior = $"Estatus: {valorAnterior}", ValorNuevo = $"Estatus: {dto.NuevoEstatus}", FechaCambio = DateTime.Now };
            _context.BitacoraAuditoria.Add(bitacora);
            await _context.SaveChangesAsync();

            return Ok(new { mensaje = $"La cita se ha marcado como {dto.NuevoEstatus} correctamente." });
        }

        private async Task<bool> ValidarReCaptcha(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;

            // AHORA SE LEE DESDE LOS SECRETOS DE CONFIGURACIÓN
            var secret = _config["RecaptchaSettings:SecretKey"];

            using var client = new HttpClient();
            var response = await client.PostAsync($"https://www.google.com/recaptcha/api/siteverify?secret={secret}&response={token}", null);
            var json = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("success").GetBoolean();
        }
    }

    public class CambioEstatusDTO { public string NuevoEstatus { get; set; } public int IdUsuarioInterno { get; set; } }
    public class ReagendarDTO { public DateTime NuevaFechaHora { get; set; } }
    public class CitaDTO
    {
        public string Curp { get; set; }
        public string Nombre { get; set; }
        public string Correo { get; set; }
        public string Telefono { get; set; }
        public string MunicipioRegistro { get; set; }
        public string EstadoRegistro { get; set; }
        public int IdTramite { get; set; }
        public int IdSede { get; set; }
        public DateTime FechaHora { get; set; }
        public string Navegador { get; set; }
        public string SistemaOperativo { get; set; }
        public string CaptchaToken { get; set; }
    }
}