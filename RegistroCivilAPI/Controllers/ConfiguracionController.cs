using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;
using RegistroCivilAPI.Services;

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
            string valorAnterior = rango == null ? "No configurado" : $"Inicio: {rango.FechaInicio:yyyy-MM-dd}; Fin: {rango.FechaFin:yyyy-MM-dd}";
            if (rango == null)
            {
                rango = new ConfiguracionAgenda { Id = 1, FechaInicio = dto.FechaInicio, FechaFin = dto.FechaFin };
                _context.ConfiguracionAgendas.Add(rango);
            }
            else
            {
                rango.FechaInicio = dto.FechaInicio; rango.FechaFin = dto.FechaFin;
            }
            _context.BitacoraAuditoria.Add(await BitacoraHelper.CrearAsync(User, _context, "Configuracion_Agenda", "UPDATE", "1", valorAnterior, $"Inicio: {dto.FechaInicio:yyyy-MM-dd}; Fin: {dto.FechaFin:yyyy-MM-dd}"));
            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Rango de fechas de reservación actualizado." });
        }
        [Authorize(Roles = "Administrador,Super Administrador,Dirección")]
        [HttpPost("DiasInhabiles")]
        public async Task<ActionResult> AddDiaInhabil([FromBody] DiaInhabilDTO dto)
        {
            var nuevoDia = new DiasInhabile { FechaBloqueada = DateOnly.FromDateTime(dto.Fecha), Motivo = dto.Motivo, IdSede = null };
            _context.DiasInhabiles.Add(nuevoDia);
            _context.BitacoraAuditoria.Add(await BitacoraHelper.CrearAsync(User, _context, "Dias_Inhabiles", "INSERT", dto.Fecha.ToString("yyyy-MM-dd"), null, $"Motivo: {dto.Motivo}"));
            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Día inhábil registrado correctamente. El calendario ha sido bloqueado." });
        }

        [Authorize(Roles = "Administrador,Super Administrador,Dirección")]
        [HttpDelete("DiasInhabiles/{id}")]
        public async Task<ActionResult> DeleteDiaInhabil(int id)
        {
            var dia = await _context.DiasInhabiles.FindAsync(id);
            if (dia == null) return NotFound();
            _context.DiasInhabiles.Remove(dia);
            _context.BitacoraAuditoria.Add(await BitacoraHelper.CrearAsync(User, _context, "Dias_Inhabiles", "DELETE", id.ToString(), $"Fecha: {dia.FechaBloqueada:yyyy-MM-dd}; Motivo: {dia.Motivo}", null));
            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Día inhábil eliminado. El día vuelve a estar disponible." });
        }
    }
    public class DiaInhabilDTO { public DateTime Fecha { get; set; } public string Motivo { get; set; } }
}
