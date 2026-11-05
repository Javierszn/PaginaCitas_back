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
using RegistroCivilAPI.Services;

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

[HttpGet("Roles")]
        [Authorize(Roles = "Super Administrador")]
        public async Task<ActionResult> GetRoles()
        {
            var roles = await _context.Roles.OrderBy(r => r.IdRol)
                .Select(r => new { idRol = r.IdRol, nombreRol = r.NombreRol })
                .ToListAsync();
            return Ok(roles);
        }

        [HttpPost]
        [Authorize(Roles = "Super Administrador")]
        public async Task<ActionResult> CrearUsuario([FromBody] NuevoUsuarioDTO dto)
        {
            dto.Username = dto.Username.Trim();
            dto.NombreCompleto = dto.NombreCompleto.Trim();

            if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.NombreCompleto))
                return BadRequest(new { mensaje = "El nombre de usuario y el nombre completo son obligatorios." });

            if (await _context.UsuariosInternos.AnyAsync(u => u.Username.ToUpper() == dto.Username.ToUpper()))
                return BadRequest(new { mensaje = "El nombre de usuario ya existe." });

            var rol = await _context.Roles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.IdRol == dto.IdRol);
            if (rol == null)
                return BadRequest(new { mensaje = "El rol seleccionado no existe." });

            var sede = await _context.Sedes
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.IdSede == dto.IdSede);
            if (sede == null)
                return BadRequest(new { mensaje = "La sede seleccionada no existe o ya no está disponible." });

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
            var detalleCreacion = $"Usuario: {usr.Username}; nombre: {usr.NombreCompleto}; rol: {rol.NombreRol}; sede: {sede.Nombre}";
            _context.BitacoraAuditoria.Add(await BitacoraHelper.CrearAsync(User, _context, "Usuarios_Internos", "INSERT", dto.Username, null, detalleCreacion));
            await _context.SaveChangesAsync();

            return Ok(new { mensaje = "Usuario creado exitosamente." });
        }

        [HttpPut("{id}/estado")]
        [Authorize(Roles = "Super Administrador")]
        public async Task<ActionResult> ToggleEstado(int id)
        {
            var usr = await _context.UsuariosInternos.FindAsync(id);
            if (usr == null) return NotFound();

            var estadoAnterior = usr.Activo;
            usr.Activo = usr.Activo != true;
            var cuentaActiva = usr.Activo == true;
            var accionCuenta = cuentaActiva ? "Cuenta activada" : "Cuenta desactivada";
            _context.BitacoraAuditoria.Add(await BitacoraHelper.CrearAsync(
                User, _context, "Usuarios_Internos", "UPDATE", id.ToString(),
                estadoAnterior == true ? "Cuenta activa" : "Cuenta desactivada",
                $"{accionCuenta}; usuario: {usr.Username}; nombre: {usr.NombreCompleto}"));
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPut("{id}/password")]
        [Authorize]
        public async Task<ActionResult> CambiarPassword(int id, [FromBody] PasswordDTO dto)
        {
            
            var esSuperAdmin = User.IsInRole("Super Administrador");
            var idClaim = User.Claims
                .Where(claim => claim.Type == ClaimTypes.NameIdentifier
                    || string.Equals(claim.Type, "nameid", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(claim.Type, "nameidentifier", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(claim.Type, "id_usuario", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(claim.Type, "IdUsuario", StringComparison.OrdinalIgnoreCase))
                .Select(claim => int.TryParse(claim.Value, out var parsedId) ? (int?)parsedId : null)
                .FirstOrDefault(parsedId => parsedId.HasValue);

            if (!esSuperAdmin)
            {
                if (idClaim != id)
                {
                    return Forbid();
                }
            }

            var usr = await _context.UsuariosInternos.FindAsync(id);
            if (usr == null) return NotFound(new { mensaje = "Usuario no encontrado." });

            usr.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
            bool requiereCambioEnSiguienteIngreso = esSuperAdmin && idClaim != id;
            usr.RequiereCambioPassword = requiereCambioEnSiguienteIngreso;
            string detalleBitacora = requiereCambioEnSiguienteIngreso
                ? $"Contraseña temporal restablecida para {usr.Username}; nombre: {usr.NombreCompleto}. Se solicitará una contraseña personal al ingresar."
                : $"Contraseña personal actualizada para {usr.Username}; nombre: {usr.NombreCompleto}.";
            _context.BitacoraAuditoria.Add(await BitacoraHelper.CrearAsync(User, _context, "Usuarios_Internos", "UPDATE", id.ToString(), null, detalleBitacora));

            await _context.SaveChangesAsync();
            return Ok(new { mensaje = requiereCambioEnSiguienteIngreso
                ? "Contraseña temporal actualizada. El usuario deberá crear su contraseña personal al iniciar sesión."
                : "Contraseña actualizada correctamente." });
        }

        [HttpPut("{id}/sede")]
        [Authorize(Roles = "Super Administrador")]
        public async Task<ActionResult> CambiarSede(int id, [FromBody] SedeDTO dto)
        {
            var usr = await _context.UsuariosInternos.FindAsync(id);
            if (usr == null) return NotFound();

            var sedeAnterior = await _context.Sedes
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.IdSede == usr.IdSede);
            var sedeNueva = await _context.Sedes
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.IdSede == dto.IdSede);
            if (sedeNueva == null)
                return BadRequest(new { mensaje = "La sede seleccionada no existe o ya no está disponible." });

            usr.IdSede = dto.IdSede;
            var anterior = sedeAnterior?.Nombre ?? "Sin sede registrada";
            var nuevo = sedeNueva.Nombre;
            _context.BitacoraAuditoria.Add(await BitacoraHelper.CrearAsync(
                User, _context, "Usuarios_Internos", "UPDATE", id.ToString(),
                $"Usuario: {usr.Username}; nombre: {usr.NombreCompleto}; sede: {anterior}",
                $"Usuario: {usr.Username}; nombre: {usr.NombreCompleto}; sede: {nuevo}"));
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
        public async Task<ActionResult> GetAccesos([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? fecha = null, [FromQuery] string? busqueda = null, [FromQuery] int? idSede = null)
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

            if (idSede.HasValue && idSede.Value > 0)
            {
                var usernamesDeSede = _context.UsuariosInternos
                    .Where(u => u.IdSede == idSede.Value)
                    .Select(u => u.Username);
                query = query.Where(a => usernamesDeSede.Contains(a.Username));
            }

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
            _context.BitacoraAuditoria.Add(await BitacoraHelper.CrearAsync(User, _context, "Registro_Accesos", "UPDATE", idAcceso.ToString(), "Sesión abierta", "Sesión cerrada remotamente"));
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

