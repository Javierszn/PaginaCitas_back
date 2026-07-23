using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RegistroCivilAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsuariosController : ControllerBase
    {
        private readonly RegistroCivilCitasContext _context;

        public UsuariosController(RegistroCivilCitasContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetUsuarios()
        {
            return await _context.UsuariosInternos
                .Include(u => u.IdRolNavigation)
                .Include(u => u.IdSedeNavigation)
                .Select(u => new
                {
                    u.IdUsuario,
                    u.Username,
                    u.NombreCompleto,
                    u.Activo,
                    u.IdSede,
                    Rol = u.IdRolNavigation.NombreRol,
                    Sede = u.IdSedeNavigation.Nombre
                }).ToListAsync();
        }

        [HttpPost]
        public async Task<ActionResult> CrearUsuario([FromBody] NuevoUsuarioDTO dto)
        {
            if (await _context.UsuariosInternos.AnyAsync(u => u.Username == dto.Username))
                return BadRequest(new { mensaje = "El nombre de usuario ya existe." });

            var usr = new UsuariosInterno
            {
                Username = dto.Username,
                NombreCompleto = dto.NombreCompleto,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                IdRol = dto.IdRol,
                IdSede = dto.IdSede,
                Activo = true,
                RequiereCambioPassword = true
            };

            _context.UsuariosInternos.Add(usr);
            await _context.SaveChangesAsync();

            return Ok(new { mensaje = "Usuario creado exitosamente." });
        }

        [HttpPut("{id}/estado")]
        public async Task<ActionResult> ToggleEstado(int id)
        {
            var usr = await _context.UsuariosInternos.FindAsync(id);
            if (usr == null) return NotFound();

            usr.Activo = !usr.Activo;
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPut("{id}/password")]
        public async Task<ActionResult> CambiarPassword(int id, [FromBody] PasswordDTO dto)
        {
            var usr = await _context.UsuariosInternos.FindAsync(id);
            if (usr == null) return NotFound(new { mensaje = "Usuario no encontrado." });

            usr.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
            usr.RequiereCambioPassword = false; // ESTA ES LA LÍNEA QUE ARREGLA EL BUG

            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Contraseña actualizada correctamente." });
        }

        [HttpPut("{id}/sede")]
        public async Task<ActionResult> CambiarSede(int id, [FromBody] SedeDTO dto)
        {
            var usr = await _context.UsuariosInternos.FindAsync(id);
            if (usr == null) return NotFound();

            usr.IdSede = dto.IdSede;
            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Sede actualizada." });
        }

        [HttpGet("Soporte")]
        public async Task<ActionResult> GetUsuariosSoporte()
        {
            var usuarios = await _context.UsuariosInternos
                .Select(u => new { u.Username, u.NombreCompleto })
                .ToListAsync();
            return Ok(usuarios);
        }

        [HttpGet("Accesos")]
        public async Task<ActionResult> GetAccesos([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? fecha = null, [FromQuery] string? busqueda = null)
        {
            var query = _context.RegistroAccesos.AsQueryable();

            if (!string.IsNullOrWhiteSpace(busqueda))
            {
                query = query.Where(a => a.Username.Contains(busqueda) || a.IdAcceso.ToString() == busqueda);
            }
            else if (!string.IsNullOrWhiteSpace(fecha) && System.DateTime.TryParse(fecha, out System.DateTime parsedDate))
            {
                query = query.Where(a => a.FechaLogin.HasValue && a.FechaLogin.Value.Date == parsedDate.Date);
            }

            int total = await query.CountAsync();
            var accesos = await query.OrderByDescending(a => a.FechaLogin)
                                     .Skip((page - 1) * pageSize)
                                     .Take(pageSize)
                                     .ToListAsync();

            return Ok(new
            {
                TotalRegistros = total,
                PaginaActual = page,
                TotalPaginas = (int)System.Math.Ceiling((double)total / pageSize),
                Datos = accesos
            });
        }
    }

    public class NuevoUsuarioDTO { public string Username { get; set; } public string Password { get; set; } public string NombreCompleto { get; set; } public int IdRol { get; set; } public int IdSede { get; set; } }
    public class PasswordDTO { public string Password { get; set; } }
    public class SedeDTO { public int IdSede { get; set; } }
}