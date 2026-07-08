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

        [HttpPost]
        public async Task<ActionResult> AgendarCita([FromBody] CitaDTO solicitud)
        {
            try
            {
                // 1. Buscamos al ciudadano por CURP
                var ciudadano = await _context.Ciudadanos
                    .FirstOrDefaultAsync(c => c.Curp == solicitud.Curp);

                if (ciudadano == null)
                {
                    return BadRequest(new { mensaje = "El CURP no se encuentra registrado." });
                }

                // 2. Validación Anti-Coyote (mismo día y ciudadano)
                var citaExistente = await _context.Citas
                    .AnyAsync(c => c.IdCiudadano == ciudadano.IdCiudadano && c.Estatus == "AGENDADA");

                if (citaExistente)
                {
                    return BadRequest(new { mensaje = "Alerta: Este ciudadano ya tiene una cita activa." });
                }

                // 3. Crear la Cita
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
        public int IdTramite { get; set; }
        public int IdSede { get; set; }
        public DateTime FechaHora { get; set; }
    }
}