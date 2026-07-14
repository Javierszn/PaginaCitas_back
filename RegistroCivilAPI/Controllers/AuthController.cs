using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;

namespace RegistroCivilAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly RegistroCivilCitasContext _context;

        public AuthController(RegistroCivilCitasContext context)
        {
            _context = context;
        }

        [HttpPost("login")]
        public async Task<ActionResult> Login([FromBody] LoginDTO credenciales)
        {
            
            var usuario = await _context.UsuariosInternos
                .Include(u => u.IdRolNavigation)
                .Include(u => u.IdSedeNavigation)
                .FirstOrDefaultAsync(u => u.Username == credenciales.Username && u.Activo == true);

           
            if (usuario == null)
            {
                return Unauthorized(new { mensaje = "Usuario no encontrado o cuenta inactiva." });
            }

        
            if (usuario.PasswordHash != credenciales.Password)
            {
                return Unauthorized(new { mensaje = "Contraseña incorrecta." });
            }

            
            return Ok(new
            {
                idUsuario = usuario.IdUsuario,
                nombre = usuario.NombreCompleto,
                rol = usuario.IdRolNavigation.NombreRol,
                idSede = usuario.IdSede,
                sede = usuario.IdSedeNavigation.Nombre
            });
        }
    }

  
    public class LoginDTO
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }
}