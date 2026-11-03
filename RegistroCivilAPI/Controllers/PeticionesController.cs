using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistroCivilAPI.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Net.Http;
using System.Text.Json;
using RegistroCivilAPI.Services;

namespace RegistroCivilAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PeticionesController : ControllerBase
    {
        private readonly RegistroCivilCitasContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<PeticionesController> _logger;

        public PeticionesController(RegistroCivilCitasContext context, IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<PeticionesController> logger)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
        }

        [HttpGet]
        [Authorize(Roles = "Super Administrador")]
        public async Task<ActionResult> GetPeticiones()
        {
            var peticiones = new List<object>();
            using (var command = _context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = "SELECT id_peticion, username_solicitante, tipo_peticion, descripcion, estatus, fecha_solicitud, respuesta, leido FROM Peticiones_Soporte ORDER BY fecha_solicitud DESC";
                await _context.Database.OpenConnectionAsync();

                using (var result = await command.ExecuteReaderAsync())
                {
                    while (await result.ReadAsync())
                    {
                        peticiones.Add(new
                        {
                            idPeticion = result.GetInt32(0),
                            username = result.GetString(1),
                            tipo = result.GetString(2),
                            descripcion = result.GetString(3),
                            estatus = result.GetString(4),
                            fecha = result.GetDateTime(5),
                            respuesta = result.IsDBNull(6) ? "" : result.GetString(6),
                            leido = result.IsDBNull(7) ? false : result.GetBoolean(7)
                        });
                    }
                }
            }
            return Ok(peticiones);
        }

        [HttpGet("MisPeticiones/{username}")]
        [Authorize]
        public async Task<ActionResult> GetMisPeticiones(string username)
        {
            var tokenUsername = User.FindFirst(ClaimTypes.Name)?.Value;
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            if (userRole != "Super Administrador" && !string.Equals(tokenUsername, username, StringComparison.OrdinalIgnoreCase))
                return StatusCode(403, new { mensaje = "Acceso denegado. Solo puedes consultar tus propias peticiones de soporte." });

            var peticiones = new List<object>();
            using (var command = _context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = "SELECT id_peticion, tipo_peticion, descripcion, estatus, fecha_solicitud, respuesta, leido FROM Peticiones_Soporte WHERE username_solicitante = @username ORDER BY fecha_solicitud DESC";
                var paramUsername = command.CreateParameter();
                paramUsername.ParameterName = "@username";
                paramUsername.Value = username;
                command.Parameters.Add(paramUsername);

                await _context.Database.OpenConnectionAsync();

                using (var result = await command.ExecuteReaderAsync())
                {
                    while (await result.ReadAsync())
                    {
                        peticiones.Add(new
                        {
                            idPeticion = result.GetInt32(0),
                            tipo = result.GetString(1),
                            descripcion = result.GetString(2),
                            estatus = result.GetString(3),
                            fecha = result.GetDateTime(4),
                            respuesta = result.IsDBNull(5) ? "" : result.GetString(5),
                            leido = result.IsDBNull(6) ? false : result.GetBoolean(6)
                        });
                    }
                }
            }
            return Ok(peticiones);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<ActionResult> CreatePeticion([FromQuery] string captchaToken, [FromBody] NuevaPeticionDTO dto)
        {
            if (string.IsNullOrEmpty(captchaToken))
                return BadRequest(new { mensaje = "Falta el token de seguridad reCAPTCHA." });

            if (dto == null || string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Tipo))
                return BadRequest(new { mensaje = "Faltan datos para registrar la solicitud." });

            var secretKey = _config["RecaptchaSettings:SecretKey"];
            var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsync($"https://www.google.com/recaptcha/api/siteverify?secret={secretKey}&response={captchaToken}", null);

            var jsonString = await response.Content.ReadAsStringAsync();
            using var captchaDocument = JsonDocument.Parse(jsonString);
            if (!captchaDocument.RootElement.TryGetProperty("success", out var captchaSuccess) || captchaSuccess.ValueKind != JsonValueKind.True)
                return BadRequest(new { mensaje = "Validación reCAPTCHA fallida. Bot detectado." });

            string username = dto.Username.Trim();
            string tipo = dto.Tipo.Trim();
            DateTime horaMexico = DateTime.UtcNow.AddHours(-6);

            if (string.Equals(tipo, "RECUPERAR CONTRASEÑA", StringComparison.OrdinalIgnoreCase))
            {
                const string mensajeRecuperacion = "Si la cuenta corresponde a un usuario registrado, la solicitud se enviará para revisión. Si no existe, no se generará un ticket.";
                bool usuarioExiste = await _context.UsuariosInternos.AnyAsync(u => u.Username == username);

                if (!usuarioExiste)
                    return Ok(new { mensaje = mensajeRecuperacion });

                await _context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Peticiones_Soporte (username_solicitante, tipo_peticion, descripcion, fecha_solicitud) " +
                    "SELECT {0}, {1}, {2}, {3} WHERE NOT EXISTS (" +
                    "SELECT 1 FROM Peticiones_Soporte WHERE username_solicitante = {0} AND tipo_peticion = {1} AND estatus = 'PENDIENTE')",
                    username, tipo, string.IsNullOrWhiteSpace(dto.Descripcion) ? "Solicitud de recuperación de contraseña." : dto.Descripcion.Trim(), horaMexico);

                return Ok(new { mensaje = mensajeRecuperacion });
            }

            await _context.Database.ExecuteSqlRawAsync(
                "INSERT INTO Peticiones_Soporte (username_solicitante, tipo_peticion, descripcion, fecha_solicitud) VALUES ({0}, {1}, {2}, {3})",
                username, tipo, dto.Descripcion?.Trim() ?? string.Empty, horaMexico);

            return Ok(new { mensaje = "Tu solicitud ha sido enviada al departamento de Sistemas. Pronto será atendida." });
        }

        [HttpPut("{id}/resolver")]
        [Authorize(Roles = "Super Administrador")]
        public async Task<ActionResult> ResolverPeticion(int id, [FromBody] RespuestaDTO dto)
        {
            if (dto == null)
                return BadRequest(new { mensaje = "La respuesta de la petición es obligatoria." });

            // PREVENCIÓN DEL ERROR 500: Asegurar que nunca sea NULL
            string respuestaSegura = string.IsNullOrWhiteSpace(dto.Respuesta) ? "Petición resuelta por Administración." : dto.Respuesta;
            if (respuestaSegura.Length > 500)
                return BadRequest(new { mensaje = "La respuesta no puede superar los 500 caracteres." });

            object peticionExiste;
            using (var cmdExiste = _context.Database.GetDbConnection().CreateCommand())
            {
                cmdExiste.CommandText = "SELECT COUNT(1) FROM Peticiones_Soporte WHERE id_peticion = @id";
                var parametroId = cmdExiste.CreateParameter();
                parametroId.ParameterName = "@id";
                parametroId.Value = id;
                cmdExiste.Parameters.Add(parametroId);
                await _context.Database.OpenConnectionAsync();
                peticionExiste = await cmdExiste.ExecuteScalarAsync();
            }
            if (Convert.ToInt32(peticionExiste) == 0)
                return NotFound(new { mensaje = "Petición no encontrada." });

            var restablecePassword = !string.IsNullOrWhiteSpace(dto.NuevaPassword);
            string? usernameSolicitante = null;
            UsuariosInterno? usuarioSolicitante = null;

            if (restablecePassword)
            {
                using (var cmd = _context.Database.GetDbConnection().CreateCommand())
                {
                    cmd.CommandText = "SELECT username_solicitante FROM Peticiones_Soporte WHERE id_peticion = @id";
                    var p = cmd.CreateParameter(); p.ParameterName = "@id"; p.Value = id; cmd.Parameters.Add(p);
                    await _context.Database.OpenConnectionAsync();
                    var result = await cmd.ExecuteScalarAsync();
                    usernameSolicitante = result?.ToString();
                }

                if (string.IsNullOrEmpty(usernameSolicitante))
                    return NotFound(new { mensaje = "Petición no encontrada." });

                usuarioSolicitante = await _context.UsuariosInternos.FirstOrDefaultAsync(u => u.Username == usernameSolicitante);
                if (usuarioSolicitante == null)
                    return NotFound(new { mensaje = "El usuario solicitante ya no existe en el sistema." });

                usuarioSolicitante.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NuevaPassword!);
                usuarioSolicitante.RequiereCambioPassword = true;
                await _context.SaveChangesAsync();
            }

            try
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "UPDATE Peticiones_Soporte SET estatus = 'RESUELTA', respuesta = {0}, leido = 0 WHERE id_peticion = {1}",
                    respuestaSegura, id);
                var detalleResolucion = restablecePassword
                    ? $"Petición resuelta; contraseña temporal restablecida para {usuarioSolicitante!.Username}; nombre: {usuarioSolicitante.NombreCompleto}. Se solicitará una contraseña personal al ingresar."
                    : $"Petición resuelta para {usernameSolicitante ?? "usuario solicitante"}.";
                _context.BitacoraAuditoria.Add(await BitacoraHelper.CrearAsync(User, _context, "Peticiones_Soporte", "UPDATE", id.ToString(), "PENDIENTE", detalleResolucion));
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al resolver la petición de soporte {IdPeticion}.", id);
                return StatusCode(500, new { mensaje = "No se pudo resolver la petición por un error interno. Revisa los registros del servidor." });
            }

            return Ok(new { mensaje = restablecePassword ? "Contraseña restablecida y petición resuelta." : "Petición marcada como resuelta y mensaje enviado al usuario." });
        }

        [HttpPut("MarcarLeidasAdmin")]
        [Authorize(Roles = "Super Administrador")]
        public async Task<ActionResult> MarcarLeidasAdmin()
        {
            await _context.Database.ExecuteSqlRawAsync("UPDATE Peticiones_Soporte SET leido = 1 WHERE estatus = 'PENDIENTE'");
            return Ok();
        }

        [HttpPut("MarcarLeidasUsuario/{username}")]
        [Authorize]
        public async Task<ActionResult> MarcarLeidasUsuario(string username)
        {
            var tokenUsername = User.FindFirst(ClaimTypes.Name)?.Value;
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            if (userRole != "Super Administrador" && !string.Equals(tokenUsername, username, StringComparison.OrdinalIgnoreCase))
                return StatusCode(403, new { mensaje = "Acceso denegado. No tienes permisos para alterar este registro." });

            await _context.Database.ExecuteSqlRawAsync("UPDATE Peticiones_Soporte SET leido = 1 WHERE username_solicitante = {0} AND estatus = 'RESUELTA'", username);
            return Ok();
        }
    }

    public class NuevaPeticionDTO { public string Username { get; set; } public string Tipo { get; set; } public string Descripcion { get; set; } }
    public class RespuestaDTO
    {
        public string Respuesta { get; set; } = string.Empty;
        public string? NuevaPassword { get; set; }
    }
}
