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

        private static List<string> ObtenerVariantesTelefono(string? telefono)
        {
            string digitos = new string((telefono ?? string.Empty).Where(char.IsDigit).ToArray());
            var variantes = new HashSet<string>(StringComparer.Ordinal) { digitos };

            // Compatibilidad entre registros mexicanos guardados con y sin lada +52.
            if (digitos.StartsWith("52", StringComparison.Ordinal) && digitos.Length == 12)
                variantes.Add(digitos.Substring(2));
            else if (digitos.Length == 10)
                variantes.Add("52" + digitos);

            variantes.Remove(string.Empty);
            return variantes.ToList();
        }

        public CitasController(RegistroCivilCitasContext context, IConfiguration config, IEmailService emailService, IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _config = config;
            _emailService = emailService;
            _httpClientFactory = httpClientFactory;
        }

        private async Task AutoActualizarInasistenciasAsync()
        {
            // Informes debe confirmar la llegada dentro de los 25 minutos posteriores a la hora de inicio.
            // Sin confirmación se convierte en inasistencia y aplica la sanción vigente.
            DateTime limiteConfirmacionMexico = DateTime.UtcNow.AddHours(-6).AddMinutes(-25);
            await _context.Database.ExecuteSqlRawAsync(
                "UPDATE Citas SET estatus = 'NO_ASISTIO' WHERE estatus IN ('PROGRAMADA', 'REPROGRAMADA') AND fecha_hora_inicio <= {0}",
                limiteConfirmacionMexico);

            DateTime ahoraMexico = DateTime.UtcNow.AddHours(-6);
            await _context.Database.ExecuteSqlRawAsync(
                "UPDATE Citas SET estatus = 'CANCELADA' WHERE estatus IN ('CANCELADA_INHABIL', 'PRESENCIAL_PENDIENTE') AND fecha_limite_resolucion_inhabil < {0}",
                ahoraMexico);
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
            if (tramite.IdSede.HasValue && tramite.IdSede.Value != idSede) return Ok(new List<string>());
            if (tramite.IdSede.HasValue && fechaCita.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                return Ok(new List<string>());

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
                .Where(c => c.IdSede == idSede && c.IdTramite == idTramite && c.FechaHoraInicio < fechaCita.Date.AddDays(1) && c.FechaHoraFin > fechaCita.Date && (c.Estatus == "PROGRAMADA" || c.Estatus == "CONFIRMADA" || c.Estatus == "REPROGRAMADA"))
                .Select(c => new { c.FechaHoraInicio, c.FechaHoraFin }).ToListAsync();

            var horasDisponibles = new List<string>();
            TimeOnly horaActual = tramite.HoraApertura ?? horarioSede.HoraApertura;
            TimeOnly horaCierre = tramite.HoraCierre ?? horarioSede.HoraCierre;

            DateTime horaMexico = DateTime.UtcNow.AddHours(-6);
            TimeOnly now = TimeOnly.FromDateTime(horaMexico);
            DateTime hoyMexico = horaMexico.Date;

            while (horaActual < horaCierre)
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
                await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                string curpBuscado = string.IsNullOrWhiteSpace(solicitud.Curp) ? null : solicitud.Curp.Trim().ToUpperInvariant();
                string nombreBuscado = string.IsNullOrWhiteSpace(solicitud.Nombre) ? null : solicitud.Nombre.Trim();
                string telLimpio = new string((solicitud.Telefono ?? string.Empty).Where(char.IsDigit).ToArray());

                if (string.IsNullOrWhiteSpace(telLimpio) || telLimpio.Length < 8 || telLimpio.Length > 15)
                    return BadRequest(new { mensaje = "El teléfono es obligatorio e incluye la lada del país. Capture un número válido de 8 a 15 dígitos." });

                if (string.IsNullOrWhiteSpace(curpBuscado) && string.IsNullOrWhiteSpace(nombreBuscado))
                    return BadRequest(new { mensaje = "Debe proporcionar la CURP o el nombre completo para identificar a la persona." });

                Ciudadano ciudadano = null;
                if (!string.IsNullOrWhiteSpace(curpBuscado))
                    ciudadano = await _context.Ciudadanos.FirstOrDefaultAsync(c => c.Curp == curpBuscado);

                if (ciudadano == null && string.IsNullOrWhiteSpace(curpBuscado) && !string.IsNullOrWhiteSpace(nombreBuscado))
                {
                    var tokensNombre = nombreBuscado.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var candidatosNombre = await _context.Ciudadanos
                        .Where(c => c.Nombre.ToUpper() == tokensNombre[0].ToUpper())
                        .ToListAsync();
                    var coincidenciasNombre = candidatosNombre
                        .Where(c => NombreCoincide(c, nombreBuscado))
                        .ToList();

                    if (coincidenciasNombre.Count > 1)
                        return BadRequest(new { mensaje = "Hay más de un registro con ese nombre. Capture la CURP para identificar correctamente a la persona." });

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
                }
                else
                {
                    ciudadano.Telefono = telLimpio;
                }

                var telefonosParaComparar = ObtenerVariantesTelefono(telLimpio);
                var ciudadanosMismoNombreTelefono = await _context.Ciudadanos
                    .Where(c => c.Telefono != null && telefonosParaComparar.Contains(c.Telefono))
                    .ToListAsync();
                var idsCiudadanoMismoNombreTelefono = ciudadanosMismoNombreTelefono
                    .Where(c => NombreCoincide(c, nombreBuscado))
                    .Select(c => c.IdCiudadano)
                    .Append(ciudadano.IdCiudadano)
                    .Distinct()
                    .ToList();

                // El mismo teléfono no puede mantener dos citas activas del mismo trámite,
                // aunque se capture otra CURP, fecha u oficina. Al terminar/cancelar la cita,
                // el teléfono vuelve a estar disponible porque solo se consulta en citas activas.
                bool telefonoYaTieneEsteTramiteActivo = await _context.Citas.AnyAsync(c =>
                    c.IdTramite == solicitud.IdTramite &&
                    (c.Estatus == "PROGRAMADA" || c.Estatus == "CONFIRMADA" || c.Estatus == "REPROGRAMADA" ||
                     c.Estatus == "CANCELADA_INHABIL" || c.Estatus == "PRESENCIAL_PENDIENTE") &&
                    c.IdCiudadanoNavigation.Telefono != null &&
                    telefonosParaComparar.Contains(c.IdCiudadanoNavigation.Telefono));

                if (telefonoYaTieneEsteTramiteActivo)
                    return BadRequest(new { mensaje = "Este teléfono ya tiene una cita activa para este trámite. Debe resolverse antes de reservar otra." });

                DateTime limiteCastigo = DateTime.UtcNow.AddHours(-6).AddDays(-7);
                var penalizado = await _context.Citas.AnyAsync(c => c.IdCiudadano == ciudadano.IdCiudadano && c.Estatus == "NO_ASISTIO" && c.FechaHoraInicio >= limiteCastigo);
                if (penalizado)
                {
                    return BadRequest(new { mensaje = "Sistema de Penalización: Usted cuenta con una inasistencia reciente. Podrá agendar nuevas citas al transcurrir 1 semana." });
                }

                var citaMismoTramite = await _context.Citas.AnyAsync(c =>
                    idsCiudadanoMismoNombreTelefono.Contains(c.IdCiudadano) &&
                    c.IdTramite == solicitud.IdTramite &&
                    (c.Estatus == "PROGRAMADA" || c.Estatus == "CONFIRMADA" || c.Estatus == "REPROGRAMADA" ||
                     c.Estatus == "CANCELADA_INHABIL" || c.Estatus == "PRESENCIAL_PENDIENTE"));

                if (citaMismoTramite) return BadRequest(new { mensaje = "Ya existe una cita activa para este trámite con esta CURP o con el mismo nombre y teléfono. Debes resolverla antes de reservar otra." });
                var tramiteEntity = await _context.Tramites.FindAsync(solicitud.IdTramite);
                if (tramiteEntity == null) return BadRequest(new { mensaje = "Trámite no encontrado." });
                if (tramiteEntity.IdSede.HasValue && tramiteEntity.IdSede.Value != solicitud.IdSede)
                    return BadRequest(new { mensaje = "Este trámite solo está disponible en la sede de la Dirección del Registro Civil, Zona Centro." });
                if (string.Equals(tramiteEntity.NombreTramite, "CURP BIOMÉTRICA", StringComparison.OrdinalIgnoreCase) && solicitud.FechaHora.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    return BadRequest(new { mensaje = "La CURP Biométrica solo se atiende de lunes a viernes." });

                if (tramiteEntity.HoraApertura.HasValue && tramiteEntity.HoraCierre.HasValue)
                {
                    TimeOnly horaInicio = TimeOnly.FromDateTime(solicitud.FechaHora);
                    TimeOnly horaFinSolicitada = horaInicio.AddMinutes(tramiteEntity.DuracionMinutos > 0 ? tramiteEntity.DuracionMinutos : 30);
                    if (horaInicio < tramiteEntity.HoraApertura.Value || horaFinSolicitada > tramiteEntity.HoraCierre.Value)
                        return BadRequest(new { mensaje = "El horario seleccionado está fuera del horario de atención para este trámite." });
                }

                int duracion = tramiteEntity.DuracionMinutos > 0 ? tramiteEntity.DuracionMinutos : 30;
                DateTime fechaFin = solicitud.FechaHora.AddMinutes(duracion);

                string folio;

                try
                {
                    var inicioDia = solicitud.FechaHora.Date;
                    var finDia = inicioDia.AddDays(1);

                    bool telefonoYaAgendadoEnEseBloque = await _context.Citas
                        .AnyAsync(c =>
                            c.FechaHoraInicio >= inicioDia && c.FechaHoraInicio < finDia &&
                            c.FechaHoraInicio < fechaFin && c.FechaHoraFin > solicitud.FechaHora &&
                            (c.Estatus == "PROGRAMADA" || c.Estatus == "CONFIRMADA" || c.Estatus == "REPROGRAMADA" ||
                             c.Estatus == "CANCELADA_INHABIL" || c.Estatus == "PRESENCIAL_PENDIENTE") &&
                            c.IdCiudadanoNavigation.Telefono != null &&
                            telefonosParaComparar.Contains(c.IdCiudadanoNavigation.Telefono));

                    if (telefonoYaAgendadoEnEseBloque)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { mensaje = "Este teléfono ya tiene una cita activa en ese día y horario, aunque sea para otro trámite o sede." });
                    }

                    bool solapada = await _context.Citas.AnyAsync(c =>
                        c.IdSede == solicitud.IdSede &&
                        c.IdTramite == solicitud.IdTramite &&
                        (c.Estatus == "PROGRAMADA" || c.Estatus == "CONFIRMADA" || c.Estatus == "REPROGRAMADA") &&
                        c.FechaHoraInicio < fechaFin &&
                        c.FechaHoraFin > solicitud.FechaHora);

                    if (solapada)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { mensaje = "Ese horario acaba de ser tomado por otro ciudadano. Por favor seleccione otro horario disponible." });
                    }

                    bool ciudadanoYaTieneCitaEnEseHorario = await _context.Citas.AnyAsync(c =>
                        c.IdCiudadano == ciudadano.IdCiudadano &&
                        (c.Estatus == "PROGRAMADA" || c.Estatus == "CONFIRMADA" || c.Estatus == "REPROGRAMADA" ||
                         c.Estatus == "CANCELADA_INHABIL" || c.Estatus == "PRESENCIAL_PENDIENTE") &&
                        c.FechaHoraInicio < fechaFin && c.FechaHoraFin > solicitud.FechaHora);

                    if (ciudadanoYaTieneCitaEnEseHorario)
                    {
                        await transaction.RollbackAsync();
                        return Conflict(new { mensaje = "Ya tienes otra cita activa a esa hora. Elige un horario distinto." });
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
                fechaLimiteResolucion = cita.FechaLimiteResolucionInhabil,
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
            bool esCierreInstitucional = cita.Estatus == "CANCELADA_INHABIL" && cita.FechaLimiteResolucionInhabil >= DateTime.UtcNow.AddHours(-6);
            if (cita.Estatus == "CANCELADA" || cita.Estatus == "ATENDIDA" || cita.Estatus == "NO_ASISTIO") return BadRequest(new { mensaje = "La cita ya no se puede cancelar." });

            DateTime horaMexico = DateTime.UtcNow.AddHours(-6);
            if (!esCierreInstitucional && cita.FechaHoraInicio < horaMexico) return BadRequest(new { mensaje = "No se puede cancelar una cita de una fecha que ya pasó." });
            if (!esCierreInstitucional && cita.FechaHoraInicio - horaMexico < TimeSpan.FromHours(2))
                return BadRequest(new { mensaje = "Solo se puede cancelar una cita con al menos 2 horas de anticipación." });

            cita.Estatus = "CANCELADA";
            cita.FechaLimiteResolucionInhabil = null;

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

        [HttpPut("{folio}/atencion-presencial")]
        public async Task<ActionResult> ElegirAtencionPresencial(string folio)
        {
            var cita = await _context.Citas.Include(c => c.IdSedeNavigation).FirstOrDefaultAsync(c => c.IdCita == folio);
            if (cita == null) return NotFound(new { mensaje = "Cita no encontrada." });
            DateTime ahoraMexico = DateTime.UtcNow.AddHours(-6);
            if (cita.Estatus != "CANCELADA_INHABIL" || cita.FechaLimiteResolucionInhabil <= ahoraMexico)
                return BadRequest(new { mensaje = "La opción de conservar el turno presencial ya no está disponible." });

            cita.Estatus = "PRESENCIAL_PENDIENTE";
            await _context.SaveChangesAsync();
            return Ok(new { mensaje = $"Su turno quedó protegido en {cita.IdSedeNavigation?.Nombre ?? "la sede original"}. Preséntese con su folio antes del {cita.FechaLimiteResolucionInhabil:dd/MM/yyyy}; tiene siete días hábiles." });
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
            bool esCierreInstitucional = cita.Estatus == "CANCELADA_INHABIL" && cita.FechaLimiteResolucionInhabil > DateTime.UtcNow.AddHours(-6);
            if ((!esCierreInstitucional && cita.Estatus != "PROGRAMADA") || cita.Estatus == "ATENDIDA" || cita.Estatus == "NO_ASISTIO" || cita.Estatus == "CANCELADA" || cita.Estatus == "PRESENCIAL_PENDIENTE" || cita.Estatus == "REPROGRAMADA")
                return BadRequest(new { mensaje = "Esta cita no puede reagendarse con su estatus actual: " + cita.Estatus });

            DateTime horaMexicoActual = DateTime.UtcNow.AddHours(-6);
            if (dto.NuevaFechaHora <= horaMexicoActual)
                return BadRequest(new { mensaje = "La fecha y hora seleccionadas ya pasaron. Seleccione un horario futuro." });
            if (!esCierreInstitucional && cita.FechaHoraInicio <= horaMexicoActual)
                return BadRequest(new { mensaje = "No se puede reprogramar una cita cuya hora ya comenzó o pasó." });
            if (!esCierreInstitucional && cita.FechaHoraInicio - horaMexicoActual < TimeSpan.FromHours(2))
                return BadRequest(new { mensaje = "Solo se puede reprogramar una cita con al menos 2 horas de anticipación." });

            int duracion = cita.IdTramiteNavigation.DuracionMinutos > 0 ? cita.IdTramiteNavigation.DuracionMinutos : 30;
            DateTime nuevaFechaFin = dto.NuevaFechaHora.AddMinutes(duracion);

            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                bool solapada = await _context.Citas.AnyAsync(c =>
                    c.IdCita != folio &&
                    c.IdSede == cita.IdSede &&
                    c.IdTramite == cita.IdTramite &&
                    (c.Estatus == "PROGRAMADA" || c.Estatus == "CONFIRMADA" || c.Estatus == "REPROGRAMADA") &&
                    c.FechaHoraInicio < nuevaFechaFin &&
                    c.FechaHoraFin > dto.NuevaFechaHora);

                if (solapada)
                {
                    await transaction.RollbackAsync();
                    return BadRequest(new { mensaje = "Ese horario acaba de ser tomado por otro ciudadano. Por favor seleccione otro horario disponible." });
                }

                bool ciudadanoOcupadoEnNuevoHorario = await _context.Citas.AnyAsync(c =>
                    c.IdCita != folio &&
                    c.IdCiudadano == cita.IdCiudadano &&
                    (c.Estatus == "PROGRAMADA" || c.Estatus == "CONFIRMADA" || c.Estatus == "REPROGRAMADA" ||
                     c.Estatus == "CANCELADA_INHABIL" || c.Estatus == "PRESENCIAL_PENDIENTE") &&
                    c.FechaHoraInicio < nuevaFechaFin && c.FechaHoraFin > dto.NuevaFechaHora);

                if (ciudadanoOcupadoEnNuevoHorario)
                {
                    await transaction.RollbackAsync();
                    return Conflict(new { mensaje = "Ya tienes otra cita activa a esa hora. Elige un horario distinto." });
                }

                var telefonosParaComparar = ObtenerVariantesTelefono(cita.IdCiudadanoNavigation.Telefono);
                var inicioDiaNuevo = dto.NuevaFechaHora.Date;
                var finDiaNuevo = inicioDiaNuevo.AddDays(1);
                bool telefonoOcupadoEnNuevoHorario = await _context.Citas.AnyAsync(c =>
                    c.IdCita != folio &&
                    c.FechaHoraInicio >= inicioDiaNuevo && c.FechaHoraInicio < finDiaNuevo &&
                    c.FechaHoraInicio < nuevaFechaFin && c.FechaHoraFin > dto.NuevaFechaHora &&
                    (c.Estatus == "PROGRAMADA" || c.Estatus == "CONFIRMADA" || c.Estatus == "REPROGRAMADA" ||
                     c.Estatus == "CANCELADA_INHABIL" || c.Estatus == "PRESENCIAL_PENDIENTE") &&
                    c.IdCiudadanoNavigation.Telefono != null &&
                    telefonosParaComparar.Contains(c.IdCiudadanoNavigation.Telefono));

                if (telefonoOcupadoEnNuevoHorario)
                {
                    await transaction.RollbackAsync();
                    return Conflict(new { mensaje = "Este teléfono ya tiene una cita activa en ese día y horario, aunque sea para otro trámite o sede." });
                }

                cita.FechaHoraInicio = dto.NuevaFechaHora;
                cita.FechaHoraFin = nuevaFechaFin;
                cita.Estatus = "REPROGRAMADA";
                cita.FechaLimiteResolucionInhabil = null;

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
            var query = _context.Citas.Include(c => c.IdCiudadanoNavigation).Include(c => c.IdTramiteNavigation).AsQueryable();
            if (idSede > 0) query = query.Where(c => c.IdSede == idSede);

            if (!string.IsNullOrWhiteSpace(busqueda))
            {
                busqueda = busqueda.ToLower();
                query = query.Where(c => c.IdCita.ToLower().Contains(busqueda) || c.IdCiudadanoNavigation.Curp.ToLower().Contains(busqueda) || c.IdCiudadanoNavigation.Nombre.ToLower().Contains(busqueda) || c.IdCiudadanoNavigation.PrimerApellido.ToLower().Contains(busqueda));
            }
            else
            {
                DateTime fechaFiltro = DateTime.Today;
                if (!string.IsNullOrEmpty(fecha) && DateTime.TryParse(fecha, out DateTime parsedDate)) fechaFiltro = parsedDate.Date;
                DateTime ahoraMexico = DateTime.UtcNow.AddHours(-6);
                query = query.Where(c =>
                    (c.FechaHoraInicio.Year == fechaFiltro.Year && c.FechaHoraInicio.Month == fechaFiltro.Month && c.FechaHoraInicio.Day == fechaFiltro.Day) ||
                    (c.Estatus == "PRESENCIAL_PENDIENTE" && c.FechaLimiteResolucionInhabil > ahoraMexico));
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
                    sede = c.IdSedeNavigation.Nombre,
                    fechaStr = c.FechaHoraInicio.ToString("dd/MM/yyyy"),
                    hora = c.FechaHoraInicio.ToString("HH:mm"),
                  estatus = c.Estatus,
                    fechaLimiteResolucion = c.FechaLimiteResolucionInhabil,
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
            if (string.Equals(dto.NuevoEstatus, "CONFIRMADA", StringComparison.OrdinalIgnoreCase) && userRole != "Informes")
                return StatusCode(403, new { mensaje = "Solo el personal de Informes puede confirmar la llegada del ciudadano." });
            if (userRole != "Super Administrador" && userSedeClaim != cita.IdSede.ToString()) return StatusCode(403, new { mensaje = "Acceso denegado. Esta cita pertenece a otra sede." });

            string nuevoEstatus = dto.NuevoEstatus?.Trim().ToUpperInvariant() ?? string.Empty;
            bool transicionValida = (cita.Estatus == "PROGRAMADA" || cita.Estatus == "REPROGRAMADA" || cita.Estatus == "PRESENCIAL_PENDIENTE") && nuevoEstatus == "CONFIRMADA"
                || cita.Estatus == "CONFIRMADA" && (nuevoEstatus == "ATENDIDA" || nuevoEstatus == "NO_ASISTIO");
            if (!transicionValida)
                return Conflict(new { mensaje = $"No se permite cambiar la cita de {cita.Estatus} a {dto.NuevoEstatus}." });
            if (cita.Estatus == "PRESENCIAL_PENDIENTE" && cita.FechaLimiteResolucionInhabil <= DateTime.UtcNow.AddHours(-6))
                return Conflict(new { mensaje = "Venció el plazo para presentarse presencialmente." });

            var usuarioReal = await _context.UsuariosInternos.FirstOrDefaultAsync(u => u.Username == usernameClaim);
            if (usuarioReal == null) return Unauthorized(new { mensaje = "Usuario no encontrado en el sistema." });

            int idUsuarioReal = usuarioReal.IdUsuario;
            string valorAnterior = cita.Estatus;
            cita.Estatus = nuevoEstatus;
            if (valorAnterior == "PRESENCIAL_PENDIENTE")
                cita.FechaLimiteResolucionInhabil = null;

            var bitacora = new BitacoraAuditorium
            {
                IdUsuarioInterno = idUsuarioReal,
                TablaAfectada = "Citas",
                AccionRealizada = "UPDATE",
                RegistroId = folio,
                ValorAnterior = $"Estatus: {valorAnterior}",
                ValorNuevo = $"Estatus: {nuevoEstatus}",
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

            return Ok(new { mensaje = $"La cita se ha marcado como {nuevoEstatus} correctamente." });
        }

        private static bool NombreCoincide(Ciudadano ciudadano, string? nombreCapturado)
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

