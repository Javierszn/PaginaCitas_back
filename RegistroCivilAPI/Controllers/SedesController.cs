using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;

namespace RegistroCivilAPI.Controllers
{
    
    [Route("api/[controller]")]
    [ApiController]
    public class SedesController : ControllerBase
    {
       
        private readonly RegistroCivilCitasContext _context;

       
        public SedesController(RegistroCivilCitasContext context)
        {
            _context = context;
        }

      
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Sede>>> GetSedes()
        {
            
            if (_context.Sedes == null)
            {
                return NotFound("No se encontró la tabla de Sedes en la base de datos.");
            }

           
            var sedes = await _context.Sedes
                                      .Where(sede => sede.Activa == true)
                                      .ToListAsync();

            
            return Ok(sedes);
        }
    }
}