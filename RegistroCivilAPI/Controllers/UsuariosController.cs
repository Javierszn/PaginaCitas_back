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

        
        [HttpGet("Soporte")]
        public async Task<ActionResult> GetUsuariosSoporte()
        {
            var users = await _context.UsuariosInternos
                .Where(u => u.Activo == true)
                .Select(u => new { u.Username, u.NombreCompleto })
                .ToListAsync();
            return Ok(users);
        }

        [HttpGet("Accesos")]
        public async Task<ActionResult> GetAccesos([FromQuery] string? fecha = null, [FromQuery] string? busqueda = null)
        {
            var accesos = new List<object>();
            using (var cmd = _context.Database.GetDbConnection().CreateCommand())
            {
                string query = "SELECT id_acceso, username, fecha_login, fecha_logout FROM Registro_Accesos WHERE 1=1";

                if (!string.IsNullOrWhiteSpace(busqueda))
                {
                    query += " AND (username LIKE @busqueda OR CAST(id_acceso AS VARCHAR) LIKE @busqueda)";
                    var param = cmd.CreateParameter();
                    param.ParameterName = "@busqueda";
                    param.Value = $"%{busqueda}%";
                    cmd.Parameters.Add(param);
                }
                else if (!string.IsNullOrWhiteSpace(fecha))
                {
                    if (System.DateTime.TryParse(fecha, out System.DateTime fechaFiltro))
                    {
                        query += " AND CAST(fecha_login AS DATE) = @fecha";
                        var param = cmd.CreateParameter();
                        param.ParameterName = "@fecha";
                        param.Value = fechaFiltro.ToString("yyyy-MM-dd");
                        cmd.Parameters.Add(param);
                    }
                }

                query += " ORDER BY fecha_login DESC";
                cmd.CommandText = query;

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
                            fechaLogout = reader.IsDBNull(3) ? (System.DateTime?)null : reader.GetDateTime(3)
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