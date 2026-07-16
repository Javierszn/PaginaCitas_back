using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;
using System.Linq;
using System.Threading.Tasks;

namespace RegistroCivilAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TramitesController : ControllerBase
    {
        private readonly RegistroCivilCitasContext _context;

        public TramitesController(RegistroCivilCitasContext context)
        {
            _context = context;
        }

        
        [HttpGet]
        public async Task<ActionResult> GetTramitesActivos()
        {
            var categorias = await _context.CategoriasTramites
                .Where(c => c.Activa == true)
                .Select(c => new {
                    c.IdCategoria,
                    c.NombreCategoria,
                    c.Descripcion,
                    Tramites = _context.Tramites.Where(t => t.IdCategoria == c.IdCategoria && t.Activo == true).ToList()
                }).ToListAsync();

            
            return Ok(categorias.Where(c => c.Tramites.Any()));
        }

        
        [HttpGet("Admin")]
        public async Task<ActionResult> GetTramitesAdmin()
        {
            var categorias = await _context.CategoriasTramites
                .Select(c => new {
                    c.IdCategoria,
                    c.NombreCategoria,
                    c.Descripcion,
                    Tramites = _context.Tramites.Where(t => t.IdCategoria == c.IdCategoria).ToList()
                }).ToListAsync();

            return Ok(categorias);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult> UpdateTramite(int id, [FromBody] TramiteUpdateDTO dto)
        {
            var t = await _context.Tramites.FindAsync(id);
            if (t == null) return NotFound(new { mensaje = "Trámite no encontrado." });

            t.DuracionMinutos = dto.DuracionMinutos;
            t.Costo = dto.Costo;
            t.Activo = dto.Activo;
            t.LimiteDiarioSede = dto.LimiteDiario;

            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Trámite actualizado correctamente. Reglas aplicadas." });
        }
    }

    public class TramiteUpdateDTO
    {
        public int DuracionMinutos { get; set; }
        public decimal Costo { get; set; }
        public bool Activo { get; set; }
        public int LimiteDiario { get; set; }
    }
}