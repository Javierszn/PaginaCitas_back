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
        private readonly IHttpClientFactory _httpClientFactory;

        public CitasController(RegistroCivilCitasContext context, IConfiguration config, IEmailService emailService, IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _config = config;
            _emailService = emailService;
            _httpClientFactory = httpClientFactory;
        }

        private async Task AutoActualizarInasistenciasAsync()
        {
            // FIX DEL BUG DEL "PICK": Inyectar la hora UTC-6 de San Luis Potosí explícitamente
            // para evitar que SmarterASP adelante las penalizaciones 6 horas.
            DateTime horaMexico = DateTime.UtcNow.AddHours(-6);
            await _context.Database.ExecuteSqlRawAsync(
                "UPDATE Citas SET estatus = 'NO_ASISTIO' WHERE estatus IN ('PROGRAMADA', 'CONFIRMADA', 'REPROGRAMADA') AND fecha_hora_fin < {0}", horaMexico
            );
        }

        [HttpGet("Horarios")]
        public async Task<ActionResult<IEnumerable<string>>> ObtenerHorariosDisponibles(int idSede, int idTramite, [FromQuery] string fecha)
        {
            await AutoActualizarInasistenciasAsync();

            if (!DateTime.TryParse(fecha, out DateTime fechaCita))
                return BadRequest("Formato de fecha inválido.");

            DateTime hoyMexicoFecha = DateTime.UtcNow.AddHours(-6).Date;
            if (fechaCita.Date < hoyMexicoFecha) return Ok(new List<string>());

            var config = await _context.ConfiguracionAgendas.FirstOrDefaultAsync(c => c.Id == 1);
            if (config != null)
            {
                if (fechaCita.Date < config.FechaInicio.Date || fechaCita.Date > config.FechaFin.Date)
                    return Ok(new List<string>());
            }

            var inhabil = await _context.DiasInhabiles.AnyAsync(d => d.FechaBloqueada == DateOnly.FromDateTime(fechaCita) && (d.IdSede == idSede || d.IdSede == null));
            if (inhabil) return Ok(new List<string>());

            byte diaSemana = (byte)(fechaCita.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)fechaCita.DayOfWeek);
            var horarioSede = await _context.HorariosSedes.FirstOrDefaultAsync(h => h.IdSede == idSede && h.DiaSemana == diaSemana);
            if (horarioSede == null) return Ok(new List<string>());

            var tramite = await _context.Tramites.FindAsync(idTramite);
            if (tramite == null) return BadRequest("Trámite no encontrado");

            if (tramite.FechaInicioPermitida.HasValue && fechaCita.Date < tramite.FechaInicioPermitida.Value.Date)
                return Ok(new List<string>());

            if (tramite.FechaFinPermitida.HasValue && fechaCita.Date > tramite.FechaFinPermitida.Value.Date)
                return Ok(new List<string>());

            int intervalo = tramite.DuracionMinutos > 0 ? tramite.DuracionMinutos : 30;
            int limiteDiario = tramite.LimiteDiarioSede > 0 ? tramite.LimiteDiarioSede : 999;

            var cantidadCitasDia = await _context.Citas
                .CountAsync(c => c.IdSede == idSede && c.IdTramite == idTramite && c.FechaHoraInicio.Date == fechaCita.Date && (c.Estatus == "PROGRAMADA" || c.Estatus == "CONFIRMADA" || c.Estatus == "REPROGRAMADA"));

            if (cantidadCitasDia >= limiteDiario) return Ok(new List<string>());

            var citasOcupadas = await _context.Citas
                .Where(c => c.IdSede == idSede && c.FechaHoraInicio < fechaCita.Date.AddDays(1) && c.FechaHoraFin > fechaCita.Date && (c.Estatus == "PROGRAMADA" || c.Estatus == "CONFIRMADA" || c.Estatus == "REPROGRAMADA"))
                .Select(c => new { c.FechaHoraInicio, c.FechaHoraFin }).ToListAsync();

            var horasDisponibles = new List<string>();
            TimeOnly horaActual = horarioSede.HoraApertura;

            DateTime horaMexico = DateTime.UtcNow.AddHours(-6);
            TimeOnly now = TimeOnly.FromDateTime(horaMexico);
            DateTime hoyMexico = horaMexico.Date;

            while (horaActual < horarioSede.HoraCierre)
            {
                if (fechaCita.Date == hoyMexico && horaActual <= now)
                {
                    horaActual = horaActual.AddMinutes(intervalo);
                    continue;
                }
                DateTime inicioPropuesto = fechaCita.Date.Add(horaActual.ToTimeSpan());
                DateTime finPropuesto = inicioPropuesto.AddMinutes(intervalo);
                bool horarioOcupado = citasOcupadas.Any(c => c.FechaHoraInicio < finPropuesto && c.FechaHoraFin > inicioPropuesto);
                if (!horarioOcupado)
                {
                    horasDisponibles.Add(horaActual.ToString("HH:mm"));
                }
                horaActual = horaActual.AddMinutes(intervalo);
            }
            return Ok(horasDisponibles);
        }

        [HttpPost]
        public async Task<ActionResult> AgendarCita([FromBody] CitaDTO solicitud)
        {
            if (!await ValidarReCaptcha(solicitud.CaptchaToken))
                return BadRequest(new { mensaje = "Verificación de seguridad fallida. Por favor, complete el Captcha." });

            DateTime horaMexicoActual = DateTime.UtcNow.AddHours(-6);
            if (solicitud.FechaHora <= horaMexicoActual)
                return BadRequest(new { mensaje = "La fecha y hora seleccionadas ya pasaron. Seleccione un horario futuro." });

            if (string.IsNullOrWhiteSpace(solicitud.Correo) || !System.Net.Mail.MailAddress.TryCreate(solicitud.Correo.Trim(), out _))
                return BadRequest(new { mensaje = "Escribe un correo electrónico válido para recibir la confirmación de la cita." });

            string correoDestino = solicitud.Correo.Trim();
            if (correoDestino.Length > 100)
                return BadRequest(new { mensaje = "El correo electrónico no puede superar los 100 caracteres." });

            await AutoActualizarInasistenciasAsync();

            try
            {
                string curpBuscado = string.IsNullOrWhiteSpace(solicitud.Curp) ? null : solicitud.Curp.Trim().ToUpperInvariant();
                string nombreBuscado = string.IsNullOrWhiteSpace(solicitud.Nombre) ? null : solicitud.Nombre.Trim();
                string telLimpio = new string((solicitud.Telefono ?? string.Empty).Where(char.IsDigit).ToArray());

                if (string.IsNullOrWhiteSpace(telLimpio) || telLimpio.Length < 8 || telLimpio.Length > 15)
                    return BadRequest(new { mensaje = "El teléfono es obligatorio e incluye la lada del país. Capture un número válido de 8 a 15 dígitos." });

                if (string.IsNullOrWhiteSpace(curpBuscado) && string.IsNullOrWhiteSpace(nombreBuscado))
                    return BadRequest(new { mensaje = "Debe proporcionar la CURP o el nombre completo para identificar a la persona." });

                var telefonosCoincidentes = new List<string> { telLimpio };
                if (telLimpio.StartsWith("52", StringComparison.Ordinal) && telLimpio.Length == 12)
                    telefonosCoincidentes.Add(telLimpio.Substring(2)); // Compatibilidad con teléfonos mexicanos guardados antes sin lada.

                var propietariosTelefono = await _context.Ciudadanos
                    .Where(c => telefonosCoincidentes.Contains(c.Telefono))
                    .ToListAsync();
                var idsPropietariosTelefono = propietariosTelefono
                    .Select(c => c.IdCiudadano)
                    .Distinct()
                    .ToArray();

                if (idsPropietariosTelefono.Length > 1)
                    return BadRequest(new { mensaje = "Este teléfono ya está asociado con otra persona y no se puede usar para agendar otra cita." });

                Ciudadano ciudadano = null;
                if (!string.IsNullOrWhiteSpace(curpBuscado))
                    ciudadano = await _context.Ciudadanos.FirstOrDefaultAsync(c => c.Curp == curpBuscado);

                var propietarioTelefono = propietariosTelefono.FirstOrDefault();
                if (ciudadano != null && propietarioTelefono != null && ciudadano.IdCiudadano != propietarioTelefono.IdCiudadano)
                    return BadRequest(new { mensaje = "Este teléfono ya está ligado a otra CURP y no puede utilizarse para esta persona." });

                if (ciudadano == null && propietarioTelefono != null)
                {
                    if (string.IsNullOrWhiteSpace(curpBuscado))
                    {
                        if (!NombreCoincide(propietarioTelefono, nombreBuscado))
                            return BadRequest(new { mensaje = "Este teléfono ya está ligado a otra persona. Use el teléfono registrado para esta persona." });

                        ciudadano = propietarioTelefono;
                    }
                    else if (propietarioTelefono.Curp != null &&
                             propietarioTelefono.Curp.StartsWith("ENM", StringComparison.OrdinalIgnoreCase) &&
                             !string.IsNullOrWhiteSpace(nombreBuscado) &&
                             NombreCoincide(propietarioTelefono, nombreBuscado))
                    {
                        // Si antes reservó sin CURP, asociar ese registro sintético con la CURP capturada ahora.
                        propietarioTelefono.Curp = curpBuscado;
                        ciudadano = propietarioTelefono;
                    }
                    else
                    {
                        return BadRequest(new { mensaje = "Este teléfono ya está ligado a otra persona o CURP y no puede reutilizarse." });
                    }
                }
                else if (ciudadano == null && propietarioTelefono == null && string.IsNullOrWhiteSpace(curpBuscado) && !string.IsNullOrWhiteSpace(nombreBuscado))
                {
                    var tokensNombre = nombreBuscado.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var candidatosNombre = await _context.Ciudadanos
                        .Where(c => c.Nombre.ToUpper() == tokensNombre[0].ToUpper())
                        .ToListAsync();
                    var coincidenciasNombre = candidatosNombre
                        .Where(c => NombreCoincide(c, nombreBuscado))
                        .ToList();

                    if (coincidenciasNombre.Count > 1)
                        return BadRequest(new { mensaje = "Hay más de un registro con ese nombre. Capture la CURP para identificar a la persona antes de cambiar el teléfono." });

                    if (coincidenciasNombre.Count == 1)
                        ciudadano = coincidenciasNombre[0];
                }

                if (ciudadano == null)
                {
                    string curpFinal = curpBuscado ?? "ENM" + Guid.NewGuid().ToString("N").Substring(0, 15).ToUpperInvariant();
                    string nombreSeguro = nombreBuscado ?? string.Empty;
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
                        Telefono = telLimpio,
                        OrigenRegistro = origenRegistro
                    };
                    _context.Ciudadanos.Add(ciudadano);
                    await _context.SaveChangesAsync();
                    idsPropietariosTelefono = new[] { ciudadano.IdCiudadano };
                }
                else
                {
                    ciudadano.Telefono = telLimpio;
                }

                DateTime limiteCastigo = DateTime.UtcNow.AddHours(-6).AddDays(-7);
                var penalizado = await _context.Citas.AnyAsync(c => c.IdCiudadano == ciudadano.IdCiudadano && c.Estatus == "NO_ASISTIO" && c.FechaHoraInicio >= limiteCastigo);
                if (penalizado)
                {
                    return BadRequest(new { mensaje = "Sistema de Penalización: Usted cuenta con una inasistencia reciente. Podrá agendar nuevas citas al transcurrir 1 semana." });
                }

                var citaMismoTramite = await _context.Citas.AnyAsync(c =>
                    (c.IdCiudadano == ciudadano.IdCiudadano || idsPropietariosTelefono.Contains(c.IdCiudadano)) &&
                    c.IdTramite == solicitud.IdTramite &&
                    c.FechaHoraInicio.Date == solicitud.FechaHora.Date &&
                    (c.Estatus == "PROGRAMADA" || c.Estatus == "CONFIRMADA" || c.Estatus == "REPROGRAMADA"));

                if (citaMismoTramite) return BadRequest(new { mensaje = "Alerta: Este teléfono ya tiene una cita pendiente para este trámite en este día, incluso si se realiza en otra sede." });
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
                        (c.Estatus == "PROGRAMADA" || c.Estatus == "CONFIRMADA" || c.Estatus == "REPROGRAMADA") &&
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
                ciudadano.Correo = correoDestino;
                await _context.SaveChangesAsync();
                string nombreSede = sedeEntity?.Nombre ?? "Oficina del Registro Civil";
                string requisitosTramite = tramiteEntity.Requisitos ?? "Por favor comuníquese a la sede para confirmar los requisitos obligatorios.";

                string nombreCompleto = $"{ciudadano.Nombre} {ciudadano.PrimerApellido} {ciudadano.SegundoApellido}".Trim();
                string identificadorParaCorreo = !string.IsNullOrWhiteSpace(nombreCompleto) ? nombreCompleto : $"CURP: {ciudadano.Curp}";

                await _emailService.EnviarCorreoConfirmacionAsync(correoDestino, identificadorParaCorreo, folio, solicitud.FechaHora, tramiteEntity.NombreTramite, tramiteEntity.Costo, nombreSede, requisitosTramite);

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
            var cita = await _context.Citas
                .Include(c => c.IdCiudadanoNavigation)
                .Include(c => c.IdTramiteNavigation)
                .Include(c => c.IdSedeNavigation)
                .FirstOrDefaultAsync(c => c.IdCita == folio);
            if (cita == null) return NotFound(new { mensaje = "Cita no encontrada." });
            if (cita.Estatus == "CANCELADA") return BadRequest(new { mensaje = "La cita ya se encuentra cancelada." });

            DateTime horaMexico = DateTime.UtcNow.AddHours(-6);
            if (cita.FechaHoraInicio < horaMexico) return BadRequest(new { mensaje = "No se puede cancelar una cita de una fecha que ya pasó." });
            if (cita.FechaHoraInicio - horaMexico < TimeSpan.FromHours(2))
                return BadRequest(new { mensaje = "Solo se puede cancelar una cita con al menos 2 horas de anticipación." });

            cita.Estatus = "CANCELADA";

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return StatusCode(StatusCodes.Status409Conflict, new { mensaje = "Alerta de Concurrencia: Otro empleado acaba de modificar el estatus de esta cita." });
            }

            string nombreCompleto = $"{cita.IdCiudadanoNavigation.Nombre} {cita.IdCiudadanoNavigation.PrimerApellido} {cita.IdCiudadanoNavigation.SegundoApellido}".Trim();
            string identificadorParaCorreo = !string.IsNullOrWhiteSpace(nombreCompleto) ? nombreCompleto : $"CURP: {cita.IdCiudadanoNavigation.Curp}";
            await _emailService.EnviarCorreoCancelacionAsync(
                cita.IdCiudadanoNavigation.Correo,
                identificadorParaCorreo,
                cita.IdCita,
                cita.FechaHoraInicio,
                cita.IdTramiteNavigation.NombreTramite,
                cita.IdSedeNavigation.Nombre);

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

            DateTime horaMexicoActual = DateTime.UtcNow.AddHours(-6);
            if (dto.NuevaFechaHora <= horaMexicoActual)
                return BadRequest(new { mensaje = "La fecha y hora seleccionadas ya pasaron. Seleccione un horario futuro." });
            if (cita.FechaHoraInicio <= horaMexicoActual)
                return BadRequest(new { mensaje = "No se puede reprogramar una cita cuya hora ya comenzó o pasó." });
            if (cita.FechaHoraInicio - horaMexicoActual < TimeSpan.FromHours(2))
                return BadRequest(new { mensaje = "Solo se puede reprogramar una cita con al menos 2 horas de anticipación." });

            int duracion = cita.IdTramiteNavigation.DuracionMinutos > 0 ? cita.IdTramiteNavigation.DuracionMinutos : 30;
            DateTime nuevaFechaFin = dto.NuevaFechaHora.AddMinutes(duracion);

            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                bool solapada = await _context.Citas.AnyAsync(c =>
                    c.IdCita != folio &&
                    c.IdSede == cita.IdSede &&
                    (c.Estatus == "PROGRAMADA" || c.Estatus == "CONFIRMADA" || c.Estatus == "REPROGRAMADA") &&
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
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync();
                return StatusCode(StatusCodes.Status409Conflict, new { mensaje = "Alerta de Concurrencia: Otro empleado modificó la cita." });
            }
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
        [Authorize(Roles = "Super Administrador,Administrador,Ventanilla,Empleado Ventanilla,Informes")]
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
        [Authorize(Roles = "Super Administrador,Administrador,Ventanilla,Empleado Ventanilla,Informes")]
        public async Task<ActionResult> ActualizarEstatus(string folio, [FromBody] CambioEstatusDTO dto)
        {
            var cita = await _context.Citas.FirstOrDefaultAsync(c => c.IdCita == folio);
            if (cita == null) return NotFound(new { mensaje = "Cita no encontrada." });

            var usernameClaim = User.FindFirst(ClaimTypes.Name)?.Value;
            var userSedeClaim = User.FindFirst("SedeId")?.Value;
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            if (string.IsNullOrEmpty(usernameClaim)) return Unauthorized(new { mensaje = "Token inválido." });
            if (userRole == "Dirección") return Forbid();
            if (userRole == "Informes" && !string.Equals(dto.NuevoEstatus, "CONFIRMADA", StringComparison.OrdinalIgnoreCase))
                return StatusCode(403, new { mensaje = "El rol Informes solo puede confirmar citas." });
            if (userRole != "Super Administrador" && userSedeClaim != cita.IdSede.ToString()) return StatusCode(403, new { mensaje = "Acceso denegado. Esta cita pertenece a otra sede." });

            var usuarioReal = await _context.UsuariosInternos.FirstOrDefaultAsync(u => u.Username == usernameClaim);
            if (usuarioReal == null) return Unauthorized(new { mensaje = "Usuario no encontrado en el sistema." });

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

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return StatusCode(StatusCodes.Status409Conflict, new { mensaje = "Alerta de Concurrencia: Otro empleado acaba de modificar el estatus de esta cita." });
            }

            return Ok(new { mensaje = $"La cita se ha marcado como {dto.NuevoEstatus} correctamente." });
        }

        private static bool NombreCoincide(Ciudadano ciudadano, string nombreCapturado)
        {
            if (ciudadano == null || string.IsNullOrWhiteSpace(nombreCapturado)) return false;

            static string Normalizar(string valor)
            {
                string descompuesto = valor.Normalize(System.Text.NormalizationForm.FormD);
                var caracteres = descompuesto
                    .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    .ToArray();
                return string.Join(" ", new string(caracteres).Normalize(System.Text.NormalizationForm.FormC)
                    .ToUpperInvariant()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }

            string nombreRegistrado = $"{ciudadano.Nombre} {ciudadano.PrimerApellido} {ciudadano.SegundoApellido}";
            return Normalizar(nombreRegistrado) == Normalizar(nombreCapturado);
        }
        private async Task<bool> ValidarReCaptcha(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;

            var secret = _config["RecaptchaSettings:SecretKey"];

            var client = _httpClientFactory.CreateClient();
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

