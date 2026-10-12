using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;
using System;
using System.Security.Claims;

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
        [Authorize(Roles = "Super Administrador")]
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
        [Authorize(Roles = "Super Administrador")]
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
        [Authorize(Roles = "Super Administrador")]
        public async Task<ActionResult> ToggleEstado(int id)
        {
            var usr = await _context.UsuariosInternos.FindAsync(id);
            if (usr == null) return NotFound();

            usr.Activo = !usr.Activo;
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPut("{id}/password")]
        [Authorize]
        public async Task<ActionResult> CambiarPassword(int id, [FromBody] PasswordDTO dto)
        {
            
            var esSuperAdmin = User.IsInRole("Super Administrador");
            var idClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (!esSuperAdmin)
            {
                if (idClaim == null || !int.TryParse(idClaim, out int idAutenticado) || idAutenticado != id)
                {
                    return Forbid();
                }
            }

            var usr = await _context.UsuariosInternos.FindAsync(id);
            if (usr == null) return NotFound(new { mensaje = "Usuario no encontrado." });

            usr.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
            usr.RequiereCambioPassword = false;

            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Contraseña actualizada correctamente." });
        }

        [HttpPut("{id}/sede")]
        [Authorize(Roles = "Super Administrador")]
        public async Task<ActionResult> CambiarSede(int id, [FromBody] SedeDTO dto)
        {
            var usr = await _context.UsuariosInternos.FindAsync(id);
            if (usr == null) return NotFound();

            usr.IdSede = dto.IdSede;
            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Sede actualizada." });
        }

        [HttpGet("Soporte")]
        [Authorize]  
        public async Task<ActionResult> GetUsuariosSoporte()
        {
            var usuarios = await _context.UsuariosInternos
                .Select(u => new { u.Username, u.NombreCompleto })
                .ToListAsync();
            return Ok(usuarios);
        }
        [HttpGet("Accesos")]
        [Authorize(Roles = "Super Administrador")]
        public async Task<ActionResult> GetAccesos([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? fecha = null, [FromQuery] string? busqueda = null)
        {
            DateTime horaMexico = DateTime.UtcNow.AddHours(-6);
            await _context.Database.ExecuteSqlRawAsync(
                "UPDATE Registro_Accesos SET fecha_logout = {0} WHERE fecha_logout IS NULL AND fecha_login <= {1}",
                horaMexico, horaMexico.AddHours(-8));

            await _context.Database.ExecuteSqlRawAsync(
                ";WITH SesionesAbiertas AS (" +
                "SELECT id_acceso, username, fecha_login, ROW_NUMBER() OVER (PARTITION BY username ORDER BY fecha_login DESC, id_acceso DESC) AS numero " +
                "FROM Registro_Accesos WHERE fecha_logout IS NULL) " +
                "UPDATE anterior SET fecha_logout = COALESCE(ultima.fecha_login, {0}) " +
                "FROM Registro_Accesos anterior " +
                "INNER JOIN SesionesAbiertas vieja ON vieja.id_acceso = anterior.id_acceso " +
                "INNER JOIN SesionesAbiertas ultima ON ultima.username = vieja.username AND ultima.numero = 1 " +
                "WHERE vieja.numero > 1",
                horaMexico);

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

        [HttpPut("Accesos/{idAcceso}/cerrar")]
        [Authorize(Roles = "Super Administrador")]
        public async Task<ActionResult> CerrarSesionRemota(int idAcceso)
        {
            var acceso = await _context.RegistroAccesos.FindAsync(idAcceso);
            if (acceso == null) return NotFound(new { mensaje = "Registro no encontrado." });
            if (acceso.FechaLogout != null) return BadRequest(new { mensaje = "La sesión ya se encontraba cerrada." });

            acceso.FechaLogout = DateTime.UtcNow.AddHours(-6);
            await _context.SaveChangesAsync();
            return Ok(new { mensaje = "Sesión cerrada remotamente de forma exitosa." });
        }

        [HttpGet("EstadoSesion/{username}")]
        [Authorize]
        public async Task<ActionResult> VerificarEstadoSesionUsuario(string username)
        {
            var usernameToken = User.FindFirst(ClaimTypes.Name)?.Value;
            var accesoClaim = User.FindFirst("AccesoId")?.Value;
            if (!string.Equals(usernameToken, username, StringComparison.OrdinalIgnoreCase) || !int.TryParse(accesoClaim, out int idAcceso))
                return Ok(new { activa = false });

            var acceso = await _context.RegistroAccesos
                .FirstOrDefaultAsync(a => a.IdAcceso == idAcceso && a.Username == usernameToken);

            return Ok(new { activa = acceso != null && acceso.FechaLogout == null });
        }
    }

   

    public class NuevoUsuarioDTO
    {
        [Required(ErrorMessage = "El nombre de usuario es obligatorio.")]
        public string Username { get; set; }

        [Required(ErrorMessage = "La contraseña es obligatoria.")]
        [RegularExpression(@"^(?=.*[A-Z])(?=.*\d)(?=.*[\W_]).{6,}$", ErrorMessage = "La contraseña debe tener mínimo 6 caracteres, 1 mayúscula, 1 número y 1 carácter especial.")]
        public string Password { get; set; }

        [Required(ErrorMessage = "El nombre completo es obligatorio.")]
        public string NombreCompleto { get; set; }

        public int IdRol { get; set; }
        public int IdSede { get; set; }
    }

    public class PasswordDTO
    {
        [Required(ErrorMessage = "La contraseña es obligatoria.")]
        [RegularExpression(@"^(?=.*[A-Z])(?=.*\d)(?=.*[\W_]).{6,}$", ErrorMessage = "La contraseña debe tener mínimo 6 caracteres, 1 mayúscula, 1 número y 1 carácter especial.")]
        public string Password { get; set; }
    }

    public class SedeDTO
    {
        public int IdSede { get; set; }
    }
}
