using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;

namespace RegistroCivilAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsuariosController : ControllerBase
    {
        private readonly RegistroCivilCitasContext _context;
        public UsuariosController(RegistroCivilCitasContext context) { _context = context; }

        [HttpGet]
        public async Task<ActionResult> GetUsuarios()
        {
            var users = await _context.UsuariosInternos
                .Include(u => u.IdRolNavigation)
                .Select(u => new { u.IdUsuario, u.Username, u.NombreCompleto, u.IdRol, Rol = u.IdRolNavigation.NombreRol, u.Activo }).ToListAsync();
            return Ok(users);
        }

        [HttpPost]
        public async Task<ActionResult> CreateUsuario([FromBody] NuevoUsuarioDTO dto)
        {
            var existe = await _context.UsuariosInternos.AnyAsync(u => u.Username == dto.Username);
            if (existe) return BadRequest(new { mensaje = "El nombre de usuario ya existe." });

            var n = new UsuariosInterno
            {
                Username = dto.Username,
                PasswordHash = dto.Password,
                NombreCompleto = dto.NombreCompleto,
                IdRol = dto.IdRol,
                IdSede = 1,
                Activo = true,
                RequiereCambioPassword = true 
            };
            _context.UsuariosInternos.Add(n);
            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Usuario creado. Se le pedirá cambiar la contraseña en su primer inicio de sesión." });
        }

        [HttpPut("{id}/estado")]
        public async Task<ActionResult> ToggleEstado(int id)
        {
            var u = await _context.UsuariosInternos.FindAsync(id);
            if (u == null) return NotFound();
            u.Activo = !u.Activo; await _context.SaveChangesAsync(); return Ok(new { mensaje = "Estado de usuario actualizado." });
        }

        [HttpPut("{id}/password")]
        public async Task<ActionResult> UpdatePassword(int id, [FromBody] PasswordDTO dto)
        {
            var u = await _context.UsuariosInternos.FindAsync(id);
            if (u == null) return NotFound();
            u.PasswordHash = dto.Password;
            u.RequiereCambioPassword = false; 
            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Contraseña actualizada exitosamente." });
        }
        [HttpGet("Accesos")]
        public async Task<ActionResult> GetAccesos()
        {
            var accesos = new List<object>();
            using (var cmd = _context.Database.GetDbConnection().CreateCommand())
            {
                cmd.CommandText = "SELECT id_acceso, username, fecha_login, fecha_logout FROM Registro_Accesos ORDER BY fecha_login DESC";
                await _context.Database.OpenConnectionAsync();
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        accesos.Add(new
                        {
                            idAcceso = reader.GetInt32(0),
                            username = reader.GetString(1),
                            fechaLogin = reader.GetDateTime(2),
                            fechaLogout = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3)
                        });
                    }
                }
            }
            return Ok(accesos);
        }
    }
    public class NuevoUsuarioDTO { public string Username { get; set; } public string Password { get; set; } public string NombreCompleto { get; set; } public int IdRol { get; set; } }
    public class PasswordDTO { public string Password { get; set; } }
}