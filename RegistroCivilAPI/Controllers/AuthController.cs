using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore.Storage;
using RegistroCivilAPI.Models;
using System.Data;
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
        [EnableRateLimiting("Login")]
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

            // Cierra accesos anteriores abandonados y crea el acceso actual en una sola transacción.
            int idAcceso = 0;
            DateTime horaMexico = DateTime.UtcNow.AddHours(-6);
            await using (var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                try
                {
                    await _context.Database.ExecuteSqlRawAsync(
                        "UPDATE Registro_Accesos SET fecha_logout = {0} WHERE fecha_logout IS NULL AND fecha_login <= {1}",
                        horaMexico, horaMexico.AddHours(-8));
                    await _context.Database.ExecuteSqlRawAsync(
                        "UPDATE Registro_Accesos SET fecha_logout = {0} WHERE username = {1} AND fecha_logout IS NULL",
                        horaMexico, user.Username);

                    await using var cmd = _context.Database.GetDbConnection().CreateCommand();
                    cmd.Transaction = transaction.GetDbTransaction();
                    cmd.CommandText = "INSERT INTO Registro_Accesos (username, fecha_login) OUTPUT INSERTED.id_acceso VALUES (@username, @fechaLogin)";

                    var paramUsername = cmd.CreateParameter();
                    paramUsername.ParameterName = "@username";
                    paramUsername.Value = user.Username;
                    cmd.Parameters.Add(paramUsername);

                    var paramFechaLogin = cmd.CreateParameter();
                    paramFechaLogin.ParameterName = "@fechaLogin";
                    paramFechaLogin.Value = horaMexico;
                    cmd.Parameters.Add(paramFechaLogin);

                    idAcceso = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }

            var tokenString = GenerarTokenJWT(user, idAcceso);

            // Payload original conservado
            return Ok(new
            {
                idUsuario = user.IdUsuario,
                username = user.Username,
                nombreCompleto = user.NombreCompleto,
                correo = user.Correo,
                telefono = user.Telefono,
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

            var usernameClaim = User.FindFirst(ClaimTypes.Name)?.Value;
            var accesoClaim = User.FindFirst("AccesoId")?.Value;

            if (string.IsNullOrEmpty(usernameClaim) || !int.TryParse(accesoClaim, out int idAccesoToken) || idAccesoToken != idAcceso)
                return Unauthorized();

            await _context.Database.ExecuteSqlRawAsync(
                "UPDATE Registro_Accesos SET fecha_logout = {0} WHERE id_acceso = {1} AND username = {2} AND fecha_logout IS NULL",
                DateTime.UtcNow.AddHours(-6), idAcceso, usernameClaim);

            return Ok();
        }

        private string GenerarTokenJWT(UsuariosInterno user, int idAcceso)
        {
            var jwtSettings = _config.GetSection("JwtSettings");
            var keyBytes = Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.NameIdentifier, user.IdUsuario.ToString()),
                new Claim("id_usuario", user.IdUsuario.ToString()),
                new Claim("AccesoId", idAcceso.ToString()),
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
