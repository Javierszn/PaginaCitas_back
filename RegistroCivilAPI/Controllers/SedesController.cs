using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;

namespace RegistroCivilAPI.Controllers
{
    // Esta línea define la ruta web para llegar a esta "ventanilla" (ej. http://localhost:puerto/api/Sedes)
    [Route("api/[controller]")]
    [ApiController]
    public class SedesController : ControllerBase
    {
        // Variable para conectarnos a la base de datos
        private readonly RegistroCivilCitasContext _context;

        // El constructor recibe la conexión de la base de datos automáticamente
        public SedesController(RegistroCivilCitasContext context)
        {
            _context = context;
        }

        // ==========================================
        // ENDPOINT: OBTENER TODAS LAS SEDES ACTIVAS
        // ==========================================
        // Cuando Angular haga un GET a /api/Sedes, se ejecutará este método
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Sede>>> GetSedes()
        {
            // Verificamos si la tabla de sedes existe
            if (_context.Sedes == null)
            {
                return NotFound("No se encontró la tabla de Sedes en la base de datos.");
            }

            // Hacemos una consulta SELECT a SQL Server a través de C#
            // Solo traemos las sedes donde activa == true (1)
            var sedes = await _context.Sedes
                                      .Where(sede => sede.Activa == true)
                                      .ToListAsync();

            // Devolvemos la lista de sedes lista para que Angular la consuma
            return Ok(sedes);
        }
    }
}