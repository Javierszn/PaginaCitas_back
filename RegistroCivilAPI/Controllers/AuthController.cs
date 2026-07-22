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

            bool isPasswordValid = false;

            if (user.PasswordHash.StartsWith("$2a$") || user.PasswordHash.StartsWith("$2b$") || user.PasswordHash.StartsWith("$2y$"))
            {
                isPasswordValid = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
            }
            else
            {
                if (user.PasswordHash == dto.Password)
                {
                    isPasswordValid = true;
                    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
                    await _context.SaveChangesAsync();
                }
            }

            if (!isPasswordValid) return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });

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
        public async Task<ActionResult> Logout(int idAcceso)
        {
            await _context.Database.ExecuteSqlRawAsync("UPDATE Registro_Accesos SET fecha_logout = GETDATE() WHERE id_acceso = {0}", idAcceso);
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