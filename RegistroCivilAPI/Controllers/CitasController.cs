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

        [HttpGet("Horarios")]
        public async Task<ActionResult<IEnumerable<string>>> ObtenerHorariosDisponibles(int idSede, DateTime fecha)
        {
            if (fecha.Date < DateTime.Today) return Ok(new List<string>());

            DateOnly fechaConsulta = DateOnly.FromDateTime(fecha);
            var inhabil = await _context.DiasInhabiles
                .AnyAsync(d => d.FechaBloqueada == fechaConsulta && (d.IdSede == idSede || d.IdSede == null));
            if (inhabil) return Ok(new List<string>());

            byte diaSemana = (byte)(fecha.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)fecha.DayOfWeek);
            var horarioSede = await _context.HorariosSedes
                .FirstOrDefaultAsync(h => h.IdSede == idSede && h.DiaSemana == diaSemana);

            if (horarioSede == null) return Ok(new List<string>());

            var citasOcupadas = await _context.Citas
                .Where(c => c.IdSede == idSede && c.FechaHoraInicio.Date == fecha.Date && c.Estatus == "AGENDADA")
                .Select(c => c.FechaHoraInicio)
                .ToListAsync();

            var horasOcupadas = citasOcupadas.Select(c => TimeOnly.FromDateTime(c)).ToList();
            var horasDisponibles = new List<string>();

            TimeOnly horaActual = horarioSede.HoraApertura;
            TimeOnly horaCierre = horarioSede.HoraCierre;
            TimeOnly now = TimeOnly.FromDateTime(DateTime.Now);

            while (horaActual < horaCierre)
            {
                if (fecha.Date == DateTime.Today && horaActual <= now)
                {
                    horaActual = horaActual.AddMinutes(30);
                    continue;
                }

                if (!horasOcupadas.Contains(horaActual))
                {
                    horasDisponibles.Add(horaActual.ToString("HH:mm"));
                }
                horaActual = horaActual.AddMinutes(30);
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

                if (curpBuscado != null)
                {
                    ciudadano = await _context.Ciudadanos.FirstOrDefaultAsync(c => c.Curp == curpBuscado);
                }
                else if (nombreBuscado != null)
                {
                    ciudadano = await _context.Ciudadanos.FirstOrDefaultAsync(c => c.Nombre.ToUpper() == nombreBuscado);
                }

                if (ciudadano == null)
                {
                    string curpFinal = curpBuscado ?? "ENM" + Guid.NewGuid().ToString("N").Substring(0, 15).ToUpper();
                    var partesNombre = solicitud.Nombre.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    string origenRegistro = string.IsNullOrWhiteSpace(solicitud.EstadoRegistro)
                        ? solicitud.MunicipioRegistro
                        : $"{solicitud.EstadoRegistro} - {solicitud.MunicipioRegistro}";

                    if (string.IsNullOrWhiteSpace(origenRegistro)) origenRegistro = "MANUAL";

                    // SEGUROS ANTI-ERRORES (Truncamos los textos si exceden el límite de la BD)
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

                    ciudadano = new Ciudadano
                    {
                        Curp = curpFinal,
                        Nombre = nombrePila,
                        PrimerApellido = primerAp,
                        SegundoApellido = segundoAp,
                        Correo = correo,
                        Telefono = tel,
                        OrigenRegistro = origenRegistro
                    };

                    _context.Ciudadanos.Add(ciudadano);
                    await _context.SaveChangesAsync();
                }

                var citaExistente = await _context.Citas
                    .AnyAsync(c => c.IdCiudadano == ciudadano.IdCiudadano && c.Estatus == "AGENDADA");

                if (citaExistente)
                {
                    return BadRequest(new { mensaje = "Alerta: Este ciudadano ya tiene una cita activa." });
                }

                var nuevaCita = new Cita
                {
                    IdCita = Guid.NewGuid().ToString().Substring(0, 8),
                    IdCiudadano = ciudadano.IdCiudadano,
                    IdTramite = solicitud.IdTramite,
                    IdSede = solicitud.IdSede,
                    FechaHoraInicio = solicitud.FechaHora,
                    FechaHoraFin = solicitud.FechaHora.AddMinutes(30),
                    Estatus = "AGENDADA"
                };

                _context.Citas.Add(nuevaCita);
                await _context.SaveChangesAsync();

                return Ok(new { mensaje = "Cita agendada con éxito", folio = nuevaCita.IdCita });
            }
            catch (Exception ex)
            {
                var errorReal = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return StatusCode(500, new { mensaje = "Error de base de datos", detalle = errorReal });
            }
        }
    }

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
    }
}