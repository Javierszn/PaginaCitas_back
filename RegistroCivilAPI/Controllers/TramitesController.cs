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

        public TramitesController(RegistroCivilCitasContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Tramite>>> GetTramites()
        {
            // Ya no filtramos por IdSede porque Tramite no tiene esa propiedad
            var tramites = await _context.Tramites.Where(t => t.Activo == true).ToListAsync();
            return Ok(tramites);
        }
    }
}