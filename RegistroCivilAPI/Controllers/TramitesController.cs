using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;

namespace RegistroCivilAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TramitesController : ControllerBase
    {
        private readonly RegistroCivilCitasContext _context;
        public TramitesController(RegistroCivilCitasContext context) { _context = context; }

        [HttpGet]
        public async Task<ActionResult> GetTramitesAgrupados()
        {
            var categorias = await _context.CategoriasTramites
                .Include(c => c.Tramites)
                .Where(c => c.Activa == true)
                .Select(c => new
                {
                    idCategoria = c.IdCategoria,
                    nombreCategoria = c.NombreCategoria,
                    descripcion = c.Descripcion,
                    tramites = c.Tramites.Where(t => t.Activo == true).Select(t => new
                    {
                        idTramite = t.IdTramite,
                        nombreTramite = t.NombreTramite,
                        descripcion = t.Descripcion,
                        requisitos = t.Requisitos,
                        costo = t.Costo,
                        duracionMinutos = t.DuracionMinutos
                    }).ToList()
                })
                .ToListAsync();

            return Ok(categorias);
        }
    }
}