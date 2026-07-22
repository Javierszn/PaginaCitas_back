using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace RegistroCivilAPI.Controllers
{
    [Authorize]
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
                .Include(u => u.IdSedeNavigation)
                .Select(u => new {
                    u.IdUsuario,
                    u.Username,
                    u.NombreCompleto,
                    u.IdRol,
                    Rol = u.IdRolNavigation.NombreRol,
                    u.IdSede,
                    Sede = u.IdSedeNavigation.Nombre,
                    u.Activo
                }).ToListAsync();
            return Ok(users);
        }

        [HttpPost]
        public async Task<ActionResult> CreateUsuario([FromBody] NuevoUsuarioDTO dto)
        {
            var existe = await _context.UsuariosInternos.AnyAsync(u => u.Username == dto.Username);
            if (existe) return BadRequest(new { mensaje = "El nombre de usuario ya existe." });

            
            string passwordEncriptada = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            var n = new UsuariosInterno
            {
                Username = dto.Username,
                PasswordHash = passwordEncriptada,
                NombreCompleto = dto.NombreCompleto,
                IdRol = dto.IdRol,
                IdSede = dto.IdSede,
                Activo = true,
                RequiereCambioPassword = true
            };
            _context.UsuariosInternos.Add(n);
            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Usuario creado con contraseña encriptada." });
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

           
            u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
            u.RequiereCambioPassword = false;

            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Contraseña actualizada y encriptada exitosamente." });
        }

        [HttpPut("{id}/sede")]
        public async Task<ActionResult> UpdateSede(int id, [FromBody] SedeUpdateDTO dto)
        {
            var u = await _context.UsuariosInternos.FindAsync(id);
            if (u == null) return NotFound(new { mensaje = "Usuario no encontrado." });

            u.IdSede = dto.IdSede;
            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "La sucursal del empleado fue actualizada correctamente." });
        }

        [HttpGet("Soporte")]
        public async Task<ActionResult> GetUsuariosSoporte()
        {
            var users = await _context.UsuariosInternos.Where(u => u.Activo == true).Select(u => new { u.Username, u.NombreCompleto }).ToListAsync();
            return Ok(users);
        }

        [HttpGet("Accesos")]
        public async Task<ActionResult> GetAccesos([FromQuery] string? fecha = null, [FromQuery] string? busqueda = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            var accesos = new List<object>();
            int totalRegistros = 0;
            int offset = (page - 1) * pageSize;

            using (var cmd = _context.Database.GetDbConnection().CreateCommand())
            {
                string filterQuery = " WHERE 1=1";

                if (!string.IsNullOrWhiteSpace(busqueda))
                {
                    filterQuery += " AND (username LIKE @busqueda OR CAST(id_acceso AS VARCHAR) LIKE @busqueda)";
                    var paramBusqueda = cmd.CreateParameter();
                    paramBusqueda.ParameterName = "@busqueda";
                    paramBusqueda.Value = $"%{busqueda}%";
                    cmd.Parameters.Add(paramBusqueda);
                }
                else if (!string.IsNullOrWhiteSpace(fecha))
                {
                    if (System.DateTime.TryParse(fecha, out System.DateTime fechaFiltro))
                    {
                        filterQuery += " AND CAST(fecha_login AS DATE) = @fecha";
                        var paramFecha = cmd.CreateParameter();
                        paramFecha.ParameterName = "@fecha";
                        paramFecha.Value = fechaFiltro.ToString("yyyy-MM-dd");
                        cmd.Parameters.Add(paramFecha);
                    }
                }

                cmd.CommandText = "SELECT COUNT(*) FROM Registro_Accesos" + filterQuery;
                await _context.Database.OpenConnectionAsync();
                totalRegistros = (int)await cmd.ExecuteScalarAsync();

                cmd.CommandText = $"SELECT id_acceso, username, fecha_login, fecha_logout FROM Registro_Accesos {filterQuery} ORDER BY fecha_login DESC OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY";

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

            int totalPaginas = (int)System.Math.Ceiling((double)totalRegistros / pageSize);
            return Ok(new { Datos = accesos, PaginaActual = page, TotalPaginas = totalPaginas, TotalRegistros = totalRegistros });
        }
    }

    public class NuevoUsuarioDTO { public string Username { get; set; } public string Password { get; set; } public string NombreCompleto { get; set; } public int IdRol { get; set; } public int IdSede { get; set; } }
    public class PasswordDTO { public string Password { get; set; } }
    public class SedeUpdateDTO { public int IdSede { get; set; } }
}