using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using RegistroCivilAPI.Models;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace RegistroCivilAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly RegistroCivilCitasContext _context;
        private readonly IConfiguration _config;

        public AuthController(RegistroCivilCitasContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        [HttpPost("login")]
        public async Task<ActionResult> Login([FromBody] LoginDTO dto)
        {
            var user = await _context.UsuariosInternos.Include(u => u.IdRolNavigation).Include(u => u.IdSedeNavigation)
                .FirstOrDefaultAsync(u => u.Username == dto.Username);

            if (user == null) return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });
            if (user.Activo == false) return Unauthorized(new { mensaje = "Usuario bloqueado." });

            // ====================================================================
            // ESCUDO 1: VERIFICAR SI LA CUENTA ESTÁ BLOQUEADA ANTES DE REVISAR CONTRASEÑAS
            // ====================================================================
            if (user.BloqueadoHasta.HasValue && user.BloqueadoHasta.Value > DateTime.Now)
            {
                var tiempoRestante = (int)(user.BloqueadoHasta.Value - DateTime.Now).TotalMinutes;
                return StatusCode(403, new { mensaje = $"Por seguridad, la cuenta está bloqueada. Intente de nuevo en {tiempoRestante + 1} minutos." });
            }

            bool isPasswordValid = false;
            try
            {
                // Validación original con BCrypt conservada
                isPasswordValid = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
            }
            catch
            {
                isPasswordValid = false;
            }

            if (!isPasswordValid)
            {
                // ====================================================================
                // ESCUDO 2: CONTABILIZAR EL ERROR Y BLOQUEAR SI LLEGA A 3
                // ====================================================================
                user.IntentosFallidos += 1;

                if (user.IntentosFallidos >= 3)
                {
                    user.BloqueadoHasta = DateTime.Now.AddMinutes(15);
                    await _context.SaveChangesAsync();
                    return StatusCode(403, new { mensaje = "Cuenta bloqueada temporalmente por exceso de intentos fallidos (15 min)." });
                }

                await _context.SaveChangesAsync();
                return Unauthorized(new { mensaje = $"Usuario o contraseña incorrectos. Te quedan {3 - user.IntentosFallidos} intento(s)." });
            }

            // ====================================================================
            // ESCUDO 3: SI EL LOGIN ES EXITOSO, REINICIAMOS LOS CONTADORES A CERO
            // ====================================================================
            user.IntentosFallidos = 0;
            user.BloqueadoHasta = null;
            await _context.SaveChangesAsync();

            // Consulta original conservada para registrar el acceso
            int idAcceso = 0;
            using (var cmd = _context.Database.GetDbConnection().CreateCommand())
            {
                cmd.CommandText = "INSERT INTO Registro_Accesos (username, fecha_login) OUTPUT INSERTED.id_acceso VALUES (@username, GETDATE())";

                var paramUsername = cmd.CreateParameter();
                paramUsername.ParameterName = "@username";
                paramUsername.Value = user.Username;
                cmd.Parameters.Add(paramUsername);

                await _context.Database.OpenConnectionAsync();
                idAcceso = (int)await cmd.ExecuteScalarAsync();
            }

            var tokenString = GenerarTokenJWT(user);

            // Payload original conservado
            return Ok(new
            {
                idUsuario = user.IdUsuario,
                username = user.Username,
                nombreCompleto = user.NombreCompleto,
                rol = user.IdRolNavigation.NombreRol,
                idSede = user.IdSede,
                sede = user.IdSedeNavigation.Nombre,
                requiereCambioPassword = user.RequiereCambioPassword ?? true,
                idAcceso = idAcceso,
                token = tokenString
            });
        }


        [HttpPost("logout/{idAcceso}")]
        [Authorize]
        public async Task<ActionResult> Logout(int idAcceso)
        {

            var usernameClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(usernameClaim))
                return Unauthorized();

            await _context.Database.ExecuteSqlRawAsync(
                "UPDATE Registro_Accesos SET fecha_logout = GETDATE() WHERE id_acceso = {0} AND username = {1}",
                idAcceso, usernameClaim);

            return Ok();
        }

        private string GenerarTokenJWT(UsuariosInterno user)
        {
            var jwtSettings = _config.GetSection("JwtSettings");
            var keyBytes = Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.IdUsuario.ToString()),
                new Claim(ClaimTypes.Role, user.IdRolNavigation.NombreRol),
                new Claim("SedeId", user.IdSede.ToString())
            };

            var key = new SymmetricSecurityKey(keyBytes);
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(8),
                Issuer = jwtSettings["Issuer"],
                Audience = jwtSettings["Audience"],
                SigningCredentials = creds
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);

            return tokenHandler.WriteToken(token);
        }
    }

    public class LoginDTO { public string Username { get; set; } public string Password { get; set; } }
}