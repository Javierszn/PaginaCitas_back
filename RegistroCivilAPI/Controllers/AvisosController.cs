using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;

namespace RegistroCivilAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AvisosController : ControllerBase
    {
        private readonly RegistroCivilCitasContext _context;

        public AvisosController(RegistroCivilCitasContext context)
        {
            _context = context;
        }

        [HttpGet("Activo")]
        public async Task<ActionResult> GetAvisoActivo()
        {
            var aviso = await _context.AvisosGlobales
                .Where(a => a.Activo == true)
                .FirstOrDefaultAsync();

            if (aviso == null) return NoContent();

            return Ok(aviso);
        }
    }
}