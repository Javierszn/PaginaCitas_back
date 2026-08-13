using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using RegistroCivilAPI.Models;
using RegistroCivilAPI.Services;

namespace RegistroCivilAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CitasController : ControllerBase
    {
        private readonly RegistroCivilCitasContext _context;
        private readonly IConfiguration _config;
        private readonly IEmailService _emailService;

        public CitasController(RegistroCivilCitasContext context, IConfiguration config, IEmailService emailService)
        {
            _context = context;
            _config = config;
            _emailService = emailService;
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

                var tramiteEntity = await _context.Tramites.FindAsync(solicitud.IdTramite);
                if (tramiteEntity == null) return BadRequest(new { mensaje = "Trámite no encontrado." });

                int duracion = tramiteEntity.DuracionMinutos > 0 ? tramiteEntity.DuracionMinutos : 30;
                DateTime fechaFin = solicitud.FechaHora.AddMinutes(duracion);

                string folio;

                await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                try
                {
                    bool solapada = await _context.Citas.AnyAsync(c =>
                        c.IdSede == solicitud.IdSede &&
                        (c.Estatus == "PROGRAMADA" || c.Estatus == "REPROGRAMADA") &&
                        c.FechaHoraInicio < fechaFin &&
                        c.FechaHoraFin > solicitud.FechaHora);

                    if (solapada)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { mensaje = "Ese horario acaba de ser tomado por otro ciudadano. Por favor seleccione otro horario disponible." });
                    }

                    folio = Guid.NewGuid().ToString().Substring(0, 8);
                    string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Desconocida";

                    await _context.Database.ExecuteSqlRawAsync(
                        "INSERT INTO Citas (id_cita, id_ciudadano, id_tramite, id_sede, fecha_hora_inicio, fecha_hora_fin, estatus, ip_origen, navegador, sistema_operativo) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, 'PROGRAMADA', {6}, {7}, {8})",
                        folio, ciudadano.IdCiudadano, solicitud.IdTramite, solicitud.IdSede, solicitud.FechaHora, fechaFin, ip, solicitud.Navegador ?? "Desconocido", solicitud.SistemaOperativo ?? "Desconocido");

                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }

                var sedeEntity = await _context.Sedes.FindAsync(solicitud.IdSede);
                string nombreSede = sedeEntity?.Nombre ?? "Oficina del Registro Civil";
                string requisitosTramite = tramiteEntity.Requisitos ?? "Por favor comuníquese a la sede para confirmar los requisitos obligatorios.";

                string nombreCompleto = $"{ciudadano.Nombre} {ciudadano.PrimerApellido} {ciudadano.SegundoApellido}".Trim();
                string identificadorParaCorreo = !string.IsNullOrWhiteSpace(nombreCompleto) ? nombreCompleto : $"CURP: {ciudadano.Curp}";

                await _emailService.EnviarCorreoConfirmacionAsync(ciudadano.Correo, identificadorParaCorreo, folio, solicitud.FechaHora, tramiteEntity.NombreTramite, tramiteEntity.Costo, nombreSede, requisitosTramite);

                return Ok(new { mensaje = "Cita agendada con éxito", folio = folio });
            }
            catch (Exception)
            {
                return StatusCode(500, new { mensaje = "Ha ocurrido un error interno en el servidor." });
            }
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

            cita.Estatus = "CANCELADA";

            // --- INICIO BLINDAJE DE CONCURRENCIA ---
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    mensaje = "Alerta de Concurrencia: Otro empleado acaba de modificar el estatus de esta cita. Por favor, recargue la tabla para ver la información más reciente y evitar sobreescribir datos."
                });
            }
            // --- FIN BLINDAJE DE CONCURRENCIA ---

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

            int duracion = cita.IdTramiteNavigation.DuracionMinutos > 0 ? cita.IdTramiteNavigation.DuracionMinutos : 30;
            DateTime nuevaFechaFin = dto.NuevaFechaHora.AddMinutes(duracion);

            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                bool solapada = await _context.Citas.AnyAsync(c =>
                    c.IdCita != folio &&
                    c.IdSede == cita.IdSede &&
                    (c.Estatus == "PROGRAMADA" || c.Estatus == "REPROGRAMADA") &&
                    c.FechaHoraInicio < nuevaFechaFin &&
                    c.FechaHoraFin > dto.NuevaFechaHora);

                if (solapada)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { mensaje = "Ese horario acaba de ser tomado por otro ciudadano. Por favor seleccione otro horario disponible." });
                }

                cita.FechaHoraInicio = dto.NuevaFechaHora;
                cita.FechaHoraFin = nuevaFechaFin;
                cita.Estatus = "REPROGRAMADA";

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            // --- INICIO BLINDAJE DE CONCURRENCIA ---
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync();
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    mensaje = "Alerta de Concurrencia: Otro empleado acaba de modificar el estatus de esta cita. Por favor, recargue la tabla para ver la información más reciente y evitar sobreescribir datos."
                });
            }
            // --- FIN BLINDAJE DE CONCURRENCIA ---
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            string nombreCompleto = $"{cita.IdCiudadanoNavigation.Nombre} {cita.IdCiudadanoNavigation.PrimerApellido} {cita.IdCiudadanoNavigation.SegundoApellido}".Trim();
            string identificadorParaCorreo = !string.IsNullOrWhiteSpace(nombreCompleto) ? nombreCompleto : $"CURP: {cita.IdCiudadanoNavigation.Curp}";

            await _emailService.EnviarCorreoConfirmacionAsync(cita.IdCiudadanoNavigation.Correo, identificadorParaCorreo, folio, dto.NuevaFechaHora, cita.IdTramiteNavigation.NombreTramite, cita.IdTramiteNavigation.Costo, cita.IdSedeNavigation.Nombre, cita.IdTramiteNavigation.Requisitos, true);

            return Ok(new { mensaje = "Cita reagendada con éxito." });
        }

        [HttpGet("PorSede/{idSede}")]
        [Authorize]
        public async Task<ActionResult> ObtenerCitasPorSede(int idSede, [FromQuery] string? fecha = null, [FromQuery] string? busqueda = null, [FromQuery] int pagina = 1, [FromQuery] int registrosPorPagina = 50)
        {

            var userSedeClaim = User.FindFirst("SedeId")?.Value;
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            if (userRole != "Super Administrador" && userSedeClaim != idSede.ToString())
            {
                return StatusCode(403, new { mensaje = "Acceso denegado. No tienes permisos para consultar las citas de otra sede." });
            }

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

            bool isSuperAdmin = userRole == "Super Administrador";


            int totalRegistros = await query.CountAsync();
            int totalPaginas = (int)Math.Ceiling(totalRegistros / (double)registrosPorPagina);
            int registrosASaltar = (pagina - 1) * registrosPorPagina;

            var citas = await query
                .OrderBy(c => c.FechaHoraInicio)
                .Skip(registrosASaltar)
                .Take(registrosPorPagina)
                .Select(c => new {
                    folio = c.IdCita,
                    ciudadano = $"{c.IdCiudadanoNavigation.Nombre} {c.IdCiudadanoNavigation.PrimerApellido} {c.IdCiudadanoNavigation.SegundoApellido}".Trim(),
                    curp = c.IdCiudadanoNavigation.Curp,
                    tramite = c.IdTramiteNavigation.NombreTramite,
                    fechaStr = c.FechaHoraInicio.ToString("dd/MM/yyyy"),
                    hora = c.FechaHoraInicio.ToString("HH:mm"),
                    estatus = c.Estatus,
                    ip = isSuperAdmin ? c.IpOrigen : null,
                    navegador = isSuperAdmin ? c.Navegador : null,
                    so = isSuperAdmin ? c.SistemaOperativo : null
                })
                .ToListAsync();

            return Ok(new
            {
                totalRegistros = totalRegistros,
                totalPaginas = totalPaginas,
                paginaActual = pagina,
                datos = citas
            });
        }

        [HttpPut("{folio}/actualizarEstatus")]
        [Authorize]
        public async Task<ActionResult> ActualizarEstatus(string folio, [FromBody] CambioEstatusDTO dto)
        {
            var cita = await _context.Citas.FirstOrDefaultAsync(c => c.IdCita == folio);
            if (cita == null) return NotFound(new { mensaje = "Cita no encontrada." });

            var usernameClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userSedeClaim = User.FindFirst("SedeId")?.Value;
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            if (string.IsNullOrEmpty(usernameClaim))
                return Unauthorized(new { mensaje = "Token inválido." });

            if (userRole != "Super Administrador" && userSedeClaim != cita.IdSede.ToString())
            {
                return StatusCode(403, new { mensaje = "Acceso denegado. Esta cita pertenece a otra sede." });
            }

            var usuarioReal = await _context.UsuariosInternos.FirstOrDefaultAsync(u => u.Username == usernameClaim);
            if (usuarioReal == null)
                return Unauthorized(new { mensaje = "Usuario no encontrado en el sistema." });

            int idUsuarioReal = usuarioReal.IdUsuario;

            string valorAnterior = cita.Estatus;
            cita.Estatus = dto.NuevoEstatus;

            var bitacora = new BitacoraAuditorium
            {
                IdUsuarioInterno = idUsuarioReal,
                TablaAfectada = "Citas",
                AccionRealizada = "UPDATE",
                RegistroId = folio,
                ValorAnterior = $"Estatus: {valorAnterior}",
                ValorNuevo = $"Estatus: {dto.NuevoEstatus}",
                FechaCambio = DateTime.Now
            };
            _context.BitacoraAuditoria.Add(bitacora);


            // --- INICIO BLINDAJE DE CONCURRENCIA ---
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    mensaje = "Alerta de Concurrencia: Otro empleado acaba de modificar el estatus de esta cita. Por favor, recargue la tabla para ver la información más reciente y evitar sobreescribir datos."
                });
            }
            // --- FIN BLINDAJE DE CONCURRENCIA ---

            return Ok(new { mensaje = $"La cita se ha marcado como {dto.NuevoEstatus} correctamente." });
        }

        private async Task<bool> ValidarReCaptcha(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;

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