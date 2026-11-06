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
using System.Net.Mail;

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
        private readonly IEmailService _emailService;

        public PeticionesController(RegistroCivilCitasContext context, IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<PeticionesController> logger, IEmailService emailService)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
            _emailService = emailService;
        }

        [HttpGet]
        [Authorize(Roles = "Super Administrador")]
        public async Task<ActionResult> GetPeticiones([FromQuery] int? idSede = null)
        {
            var peticiones = new List<object>();
            using (var command = _context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = "SELECT p.id_peticion, p.username_solicitante, p.tipo_peticion, p.descripcion, p.estatus, p.fecha_solicitud, p.respuesta, p.leido, u.nombre_completo, u.id_sede, u.correo " +
                    "FROM Peticiones_Soporte p LEFT JOIN Usuarios_Internos u ON u.username = p.username_solicitante " +
                    "WHERE (@idSede IS NULL OR @idSede = 0 OR u.id_sede = @idSede) ORDER BY p.fecha_solicitud DESC";
                var parametroSede = command.CreateParameter();
                parametroSede.ParameterName = "@idSede";
                parametroSede.Value = idSede.HasValue ? idSede.Value : DBNull.Value;
                command.Parameters.Add(parametroSede);
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
                            leido = result.IsDBNull(7) ? false : result.GetBoolean(7),
                            nombreCompleto = result.IsDBNull(8) ? "" : result.GetString(8),
                            idSede = result.IsDBNull(9) ? (int?)null : result.GetInt32(9),
                            correo = result.IsDBNull(10) ? "" : result.GetString(10)
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

            string? usernameSolicitante = null;
            string? estatusPeticion = null;
            using (var cmdExiste = _context.Database.GetDbConnection().CreateCommand())
            {
                cmdExiste.CommandText = "SELECT username_solicitante, estatus FROM Peticiones_Soporte WHERE id_peticion = @id";
                var parametroId = cmdExiste.CreateParameter();
                parametroId.ParameterName = "@id";
                parametroId.Value = id;
                cmdExiste.Parameters.Add(parametroId);
                await _context.Database.OpenConnectionAsync();
                using var reader = await cmdExiste.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    usernameSolicitante = reader.GetString(0);
                    estatusPeticion = reader.GetString(1);
                }
            }
            if (usernameSolicitante == null)
                return NotFound(new { mensaje = "Petición no encontrada." });
            if (!string.Equals(estatusPeticion, "PENDIENTE", StringComparison.OrdinalIgnoreCase))
                return Conflict(new { mensaje = "Esta petición ya fue atendida." });

            var restablecePassword = !string.IsNullOrWhiteSpace(dto.NuevaPassword);
            UsuariosInterno? usuarioSolicitante = null;
            await using var transaction = await _context.Database.BeginTransactionAsync();

            if (restablecePassword)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(dto.NuevaPassword!, @"^(?=.*[A-Z])(?=.*\d)(?=.*[\W_]).{6,}$"))
                    return BadRequest(new { mensaje = "La contraseña temporal debe tener al menos 6 caracteres, una mayúscula, un número y un carácter especial." });

                usuarioSolicitante = await _context.UsuariosInternos.FirstOrDefaultAsync(u => u.Username == usernameSolicitante);
                if (usuarioSolicitante == null)
                    return NotFound(new { mensaje = "El usuario solicitante ya no existe en el sistema." });
                if (string.IsNullOrWhiteSpace(usuarioSolicitante.Correo) || !MailAddress.TryCreate(usuarioSolicitante.Correo, out _))
                    return BadRequest(new { mensaje = "La cuenta no tiene un correo válido registrado. Actualice los datos de contacto del usuario antes de restablecer la contraseña." });

                usuarioSolicitante.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NuevaPassword!);
                usuarioSolicitante.RequiereCambioPassword = true;
                await _context.SaveChangesAsync();
                bool correoEnviado = await _emailService.EnviarCorreoRecuperacionAsync(usuarioSolicitante.Correo, usuarioSolicitante.NombreCompleto, usuarioSolicitante.Username, dto.NuevaPassword!);
                if (!correoEnviado)
                {
                    await transaction.RollbackAsync();
                    return StatusCode(502, new { mensaje = "No se pudo enviar el correo. No se cambió la contraseña; verifique la configuración de correo e inténtelo de nuevo." });
                }
            }

            try
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "UPDATE Peticiones_Soporte SET estatus = 'RESUELTA', respuesta = {0}, leido = 0 WHERE id_peticion = {1}",
                    restablecePassword ? "Contraseña temporal enviada al correo registrado. Deberá cambiarla al iniciar sesión." : respuestaSegura, id);
                var detalleResolucion = restablecePassword
                    ? $"Petición resuelta; contraseña temporal restablecida para {usuarioSolicitante!.Username}; nombre: {usuarioSolicitante.NombreCompleto}. Se solicitará una contraseña personal al ingresar."
                    : $"Petición resuelta para {usernameSolicitante ?? "usuario solicitante"}.";
                _context.BitacoraAuditoria.Add(await BitacoraHelper.CrearAsync(User, _context, "Peticiones_Soporte", "UPDATE", id.ToString(), "PENDIENTE", detalleResolucion));
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error al resolver la petición de soporte {IdPeticion}.", id);
                return StatusCode(500, new { mensaje = "No se pudo resolver la petición por un error interno. Revisa los registros del servidor." });
            }

            return Ok(new { mensaje = restablecePassword ? "Contraseña restablecida y enviada al correo registrado. El usuario deberá cambiarla al iniciar sesión." : "Petición marcada como resuelta y mensaje enviado al usuario." });
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
