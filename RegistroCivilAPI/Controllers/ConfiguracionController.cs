using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;

namespace RegistroCivilAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ConfiguracionController : ControllerBase
    {
        private readonly RegistroCivilCitasContext _context;

        public ConfiguracionController(RegistroCivilCitasContext context) { _context = context; }

        [HttpGet("ReglasCalendario")]
        public async Task<ActionResult> GetReglas()
        {
            var rango = await _context.ConfiguracionAgendas.FirstOrDefaultAsync(c => c.Id == 1);
            var dias = await _context.DiasInhabiles.Select(d => new { id = d.IdDiaInhabil, fecha = d.FechaBloqueada, motivo = d.Motivo }).ToListAsync();
            return Ok(new { rango = rango, diasInhabiles = dias });
        }
        [Authorize(Roles = "Administrador,Super Administrador")]
        [HttpPut("Rango")]
        public async Task<ActionResult> UpdateRango([FromBody] ConfiguracionAgenda dto)
        {
            if (dto.FechaInicio.Date >= dto.FechaFin.Date)
            {
                return BadRequest(new { mensaje = "La fecha de inicio debe ser anterior a la fecha de fin." });
            }

            var rango = await _context.ConfiguracionAgendas.FirstOrDefaultAsync(c => c.Id == 1);
            if (rango == null)
            {
                rango = new ConfiguracionAgenda { Id = 1, FechaInicio = dto.FechaInicio, FechaFin = dto.FechaFin };
                _context.ConfiguracionAgendas.Add(rango);
            }
            else
            {
                rango.FechaInicio = dto.FechaInicio; rango.FechaFin = dto.FechaFin;
            }
            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Rango de fechas de reservación actualizado." });
        }
        [Authorize(Roles = "Administrador,Super Administrador")]
        [HttpPost("DiasInhabiles")]
        public async Task<ActionResult> AddDiaInhabil([FromBody] DiaInhabilDTO dto)
        {
            var nuevoDia = new DiasInhabile { FechaBloqueada = DateOnly.FromDateTime(dto.Fecha), Motivo = dto.Motivo, IdSede = null };
            _context.DiasInhabiles.Add(nuevoDia);
            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Día inhábil registrado correctamente. El calendario ha sido bloqueado." });
        }

        [Authorize(Roles = "Administrador,Super Administrador")]
        [HttpDelete("DiasInhabiles/{id}")]
        public async Task<ActionResult> DeleteDiaInhabil(int id)
        {
            var dia = await _context.DiasInhabiles.FindAsync(id);
            if (dia == null) return NotFound();
            _context.DiasInhabiles.Remove(dia);
            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Día inhábil eliminado. El día vuelve a estar disponible." });
        }
    }
    public class DiaInhabilDTO { public DateTime Fecha { get; set; } public string Motivo { get; set; } }
}