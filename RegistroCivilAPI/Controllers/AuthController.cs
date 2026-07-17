using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;
using System.Threading.Tasks;

namespace RegistroCivilAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly RegistroCivilCitasContext _context;
        public AuthController(RegistroCivilCitasContext context) { _context = context; }

        [HttpPost("login")]
        public async Task<ActionResult> Login([FromBody] LoginDTO dto)
        {
            var user = await _context.UsuariosInternos
                .Include(u => u.IdRolNavigation)
                .Include(u => u.IdSedeNavigation)
                .FirstOrDefaultAsync(u => u.Username == dto.Username && u.PasswordHash == dto.Password);

            if (user == null) return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });
            if (user.Activo == false) return Unauthorized(new { mensaje = "Usuario bloqueado. Contacte a soporte." });

            return Ok(new
            {
                idUsuario = user.IdUsuario,
                username = user.Username,
                nombreCompleto = user.NombreCompleto,
                rol = user.IdRolNavigation.NombreRol,
                idSede = user.IdSede,
                sede = user.IdSedeNavigation.Nombre,
                requiereCambioPassword = user.RequiereCambioPassword ?? true
            });
        }
    }
    public class LoginDTO { public string Username { get; set; } public string Password { get; set; } }
}