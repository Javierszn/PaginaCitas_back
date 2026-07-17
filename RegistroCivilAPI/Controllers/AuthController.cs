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
        public AuthController(RegistroCivilCitasContext context) { _context = context; }

        [HttpPost("login")]
        public async Task<ActionResult> Login([FromBody] LoginDTO dto)
        {
            var user = await _context.UsuariosInternos.Include(u => u.IdRolNavigation).Include(u => u.IdSedeNavigation)
                .FirstOrDefaultAsync(u => u.Username == dto.Username && u.PasswordHash == dto.Password);

            if (user == null) return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });
            if (user.Activo == false) return Unauthorized(new { mensaje = "Usuario bloqueado." });

      
            int idAcceso = 0;
            using (var cmd = _context.Database.GetDbConnection().CreateCommand())
            {
                cmd.CommandText = $"INSERT INTO Registro_Accesos (username, fecha_login) OUTPUT INSERTED.id_acceso VALUES ('{user.Username}', GETDATE())";
                await _context.Database.OpenConnectionAsync();
                idAcceso = (int)await cmd.ExecuteScalarAsync();
            }

            return Ok(new
            {
                idUsuario = user.IdUsuario,
                username = user.Username,
                nombreCompleto = user.NombreCompleto,
                rol = user.IdRolNavigation.NombreRol,
                idSede = user.IdSede,
                sede = user.IdSedeNavigation.Nombre,
                requiereCambioPassword = user.RequiereCambioPassword ?? true,
                idAcceso = idAcceso 
            });
        }

        [HttpPost("logout/{idAcceso}")]
        public async Task<ActionResult> Logout(int idAcceso)
        {
            await _context.Database.ExecuteSqlRawAsync("UPDATE Registro_Accesos SET fecha_logout = GETDATE() WHERE id_acceso = {0}", idAcceso);
            return Ok();
        }
    }
    public class LoginDTO { public string Username { get; set; } public string Password { get; set; } }
}