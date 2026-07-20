using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;

namespace RegistroCivilAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CitasController : ControllerBase
    {
        private readonly RegistroCivilCitasContext _context;

        public CitasController(RegistroCivilCitasContext context)
        {
            _context = context;
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

            var inhabil = await _context.DiasInhabiles.AnyAsync(d => d.FechaBloqueada == DateOnly.FromDateTime(fecha) && (d.IdSede == idSede || d.IdSede == null));
            if (inhabil) return Ok(new List<string>());

            byte diaSemana = (byte)(fecha.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)fecha.DayOfWeek);
            var horarioSede = await _context.HorariosSedes.FirstOrDefaultAsync(h => h.IdSede == idSede && h.DiaSemana == diaSemana);
            if (horarioSede == null) return Ok(new List<string>());

            var tramite = await _context.Tramites.FindAsync(idTramite);
            if (tramite == null) return BadRequest("Trámite no encontrado");

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
            try
            {
                Ciudadano ciudadano = null;
                string curpBuscado = string.IsNullOrWhiteSpace(solicitud.Curp) ? null : solicitud.Curp.Trim().ToUpper();
                string nombreBuscado = string.IsNullOrWhiteSpace(solicitud.Nombre) ? null : solicitud.Nombre.Trim().ToUpper();

                if (curpBuscado != null) ciudadano = await _context.Ciudadanos.FirstOrDefaultAsync(c => c.Curp == curpBuscado);
                else if (nombreBuscado != null) ciudadano = await _context.Ciudadanos.FirstOrDefaultAsync(c => c.Nombre.ToUpper() == nombreBuscado);

                if (ciudadano == null)
                {
                    string curpFinal = curpBuscado ?? "ENM" + Guid.NewGuid().ToString("N").Substring(0, 15).ToUpper();
                    var partesNombre = solicitud.Nombre.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    string origenRegistro = string.IsNullOrWhiteSpace(solicitud.EstadoRegistro) ? solicitud.MunicipioRegistro : $"{solicitud.EstadoRegistro} - {solicitud.MunicipioRegistro}";
                    if (string.IsNullOrWhiteSpace(origenRegistro)) origenRegistro = "MANUAL";
                    if (origenRegistro.Length > 145) origenRegistro = origenRegistro.Substring(0, 145);

                    string nombrePila = partesNombre.Length > 0 ? partesNombre[0] : "Sin Nombre";
                    if (nombrePila.Length > 50) nombrePila = nombrePila.Substring(0, 50);

                    string primerAp = partesNombre.Length > 1 ? partesNombre[1] : "XX";
                    if (primerAp.Length > 50) primerAp = primerAp.Substring(0, 50);

                    string segundoAp = partesNombre.Length > 2 ? string.Join(" ", partesNombre.Skip(2)) : "";
                    if (segundoAp.Length > 50) segundoAp = segundoAp.Substring(0, 50);

                    string tel = solicitud.Telefono?.Trim();
                    if (tel != null && tel.Length > 15) tel = tel.Substring(0, 15);

                    string correo = solicitud.Correo?.Trim();
                    if (correo != null && correo.Length > 100) correo = correo.Substring(0, 100);

                    ciudadano = new Ciudadano { Curp = curpFinal, Nombre = nombrePila, PrimerApellido = primerAp, SegundoApellido = segundoAp, Correo = correo, Telefono = tel, OrigenRegistro = origenRegistro };
                    _context.Ciudadanos.Add(ciudadano);
                    await _context.SaveChangesAsync();
                }

                var citaMismoTramite = await _context.Citas.AnyAsync(c => c.IdCiudadano == ciudadano.IdCiudadano && c.IdTramite == solicitud.IdTramite && (c.Estatus == "PROGRAMADA" || c.Estatus == "REPROGRAMADA"));

                if (citaMismoTramite) return BadRequest(new { mensaje = "Alerta: Usted ya tiene una cita programada para este trámite específico. Por favor, seleccione otro servicio." });

                string folio = Guid.NewGuid().ToString().Substring(0, 8);
                string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Desconocida";

                await _context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Citas (id_cita, id_ciudadano, id_tramite, id_sede, fecha_hora_inicio, fecha_hora_fin, estatus, ip_origen, navegador, sistema_operativo) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, 'PROGRAMADA', {6}, {7}, {8})",
                    folio, ciudadano.IdCiudadano, solicitud.IdTramite, solicitud.IdSede, solicitud.FechaHora, solicitud.FechaHora.AddMinutes(30), ip, solicitud.Navegador ?? "Desconocido", solicitud.SistemaOperativo ?? "Desconocido");

                return Ok(new { mensaje = "Cita agendada con éxito", folio = folio });
            }
            catch (Exception ex)
            {
                var errorReal = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return StatusCode(500, new { mensaje = "Error de base de datos", detalle = errorReal });
            }
        }

        [HttpGet("{folio}")]
        public async Task<ActionResult> ObtenerCita(string folio)
        {
            await AutoActualizarInasistenciasAsync();

            var cita = await _context.Citas.Include(c => c.IdCiudadanoNavigation).Include(c => c.IdTramiteNavigation).Include(c => c.IdSedeNavigation).FirstOrDefaultAsync(c => c.IdCita == folio);
            if (cita == null) return NotFound(new { mensaje = "No se encontró ninguna cita registrada con este folio." });

            return Ok(new
            {
                folio = cita.IdCita,
                estatus = cita.Estatus,
                fecha = cita.FechaHoraInicio.ToString("yyyy-MM-dd"),
                hora = cita.FechaHoraInicio.ToString("HH:mm"),
                idTramite = cita.IdTramite,
                tramite = cita.IdTramiteNavigation.NombreTramite,
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
            var cita = await _context.Citas.FirstOrDefaultAsync(c => c.IdCita == folio);
            if (cita == null) return NotFound(new { mensaje = "Cita no encontrada." });

            if (cita.Estatus == "CANCELADA" || cita.Estatus == "ATENDIDA" || cita.Estatus == "NO_ASISTIO" || cita.Estatus == "REPROGRAMADA")
                return BadRequest(new { mensaje = "Solo se permite reprogramar la cita una vez. El estatus actual es " + cita.Estatus });

            cita.FechaHoraInicio = dto.NuevaFechaHora;
            cita.FechaHoraFin = dto.NuevaFechaHora.AddMinutes(30);
            cita.Estatus = "REPROGRAMADA";
            await _context.SaveChangesAsync();

            return Ok(new { mensaje = "Cita reagendada con éxito." });
        }

        [HttpGet("PorSede/{idSede}")]
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
    }
}