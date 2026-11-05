using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RegistroCivilAPI.Services
{
    public interface IEmailService
    {
        Task<bool> EnviarCorreoRecuperacionAsync(string correoDestino, string nombre, string username, string passwordTemporal);
        Task EnviarCorreoConfirmacionAsync(
            string? correoDestino,
            string identificador,
            string folio,
            DateTime fechaHora,
            string tramite,
            decimal costo,
            string sede,
            string? requisitos,
            bool esReagendada = false);

        Task EnviarCorreoCancelacionAsync(
            string? correoDestino,
            string identificador,
            string folio,
            DateTime fechaHora,
            string tramite,
            string sede);

        Task EnviarAvisoCierreInhabilAsync(
            string? correoDestino,
            string identificador,
            string folio,
            DateTime fechaHora,
            string tramite,
            string sede,
            DateTime fechaLimite);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<EmailService> _logger;

        public EmailService(
            IConfiguration config,
            IHttpClientFactory httpClientFactory = null,
            ILogger<EmailService> logger = null)
        {
            _config = config;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<bool> EnviarCorreoRecuperacionAsync(string correoDestino, string nombre, string username, string passwordTemporal)
        {
            if (string.IsNullOrWhiteSpace(correoDestino) || string.IsNullOrWhiteSpace(passwordTemporal)) return false;
            string apiKey = _config["EmailSettings:ApiKey"] ?? _config["EmailSettings:PasswordApp"] ?? string.Empty;
            string correoOrigen = _config["EmailSettings:Correo"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(correoOrigen))
            {
                _logger?.LogError("No se envió la recuperación: falta configurar EmailSettings:ApiKey y EmailSettings:Correo.");
                return false;
            }

            try
            {
                string nombreHtml = WebUtility.HtmlEncode(nombre);
                string usernameHtml = WebUtility.HtmlEncode(username);
                string passwordHtml = WebUtility.HtmlEncode(passwordTemporal);
                string html = $@"<div style='font-family:Arial,sans-serif;color:#333;max-width:560px;margin:auto;padding:28px;border:1px solid #ddd;border-radius:8px'>
                    <h2 style='color:#055A1C'>Restablecimiento de contraseña</h2>
                    <p>Hola {nombreHtml},</p><p>El administrador restableció la contraseña de la cuenta <b>{usernameHtml}</b>.</p>
                    <p>Contraseña temporal: <b style='font-size:18px'>{passwordHtml}</b></p>
                    <p>Al iniciar sesión deberá crear una contraseña personal nueva. Si no solicitó este cambio, contacte al administrador.</p></div>";
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
                request.Headers.Add("api-key", apiKey);
                request.Content = new StringContent(JsonSerializer.Serialize(new
                {
                    sender = new { name = "Registro Civil Citas SLP", email = correoOrigen },
                    to = new[] { new { email = correoDestino.Trim(), name = nombre } },
                    subject = "Contraseña temporal de acceso",
                    htmlContent = html
                }), Encoding.UTF8, "application/json");
                using var client = _httpClientFactory != null ? _httpClientFactory.CreateClient() : new HttpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var response = await client.SendAsync(request, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var detail = await response.Content.ReadAsStringAsync();
                    _logger?.LogError("Brevo rechazó el correo de recuperación. Código: {Status}. Detalle: {Detalle}", (int)response.StatusCode, detail);
                    return false;
                }
                _logger?.LogInformation("Correo de recuperación enviado a {Destino}.", correoDestino);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error al enviar el correo de recuperación.");
                return false;
            }
        }

        public async Task EnviarCorreoConfirmacionAsync(
            string? correoDestino,
            string identificador,
            string folio,
            DateTime fechaHora,
            string tramite,
            decimal costo,
            string sede,
            string? requisitos,
            bool esReagendada = false)
        {
            if (string.IsNullOrWhiteSpace(correoDestino))
            {
                _logger?.LogWarning("No se envió el correo: el correo destinatario está vacío.");
                return;
            }

            string apiKey = _config["EmailSettings:ApiKey"] ?? _config["EmailSettings:PasswordApp"] ?? string.Empty;
            string correoOrigen = _config["EmailSettings:Correo"] ?? string.Empty;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger?.LogError("No se envió el correo: falta configurar EmailSettings:ApiKey en Railway.");
                return;
            }

            if (string.IsNullOrWhiteSpace(correoOrigen))
            {
                _logger?.LogError("No se envió el correo: falta configurar EmailSettings:Correo.");
                return;
            }

            try
            {
                string listaRequisitosHtml = string.Empty;
                if (!string.IsNullOrWhiteSpace(requisitos))
                {
                    string[] lineas = requisitos.Split(new[] { '\n', '\r', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var linea in lineas)
                    {
                        listaRequisitosHtml += $"<li style='margin-bottom: 8px;'>{WebUtility.HtmlEncode(linea.Trim('•', ' ', '-'))}</li>";
                    }
                }

                string tituloPrincipal = esReagendada
                    ? "Confirmación de Cita Reagendada"
                    : "Confirmación de Cita Registrada";

                string textoSecundario = esReagendada
                    ? "Su cita ha sido reagendada exitosamente para una nueva fecha."
                    : "Su cita ha sido generada exitosamente.";

                // ZONA HORARIA UTC-6: Asegura que la fecha y hora mostrada corresponda a San Luis Potosí
                DateTime horaCitaMexico = fechaHora.Kind == DateTimeKind.Utc
                    ? fechaHora.AddHours(-6)
                    : fechaHora;

                string identificadorHtml = WebUtility.HtmlEncode(identificador);
                string folioHtml = WebUtility.HtmlEncode(folio);
                string tramiteHtml = WebUtility.HtmlEncode(tramite);
                string sedeHtml = WebUtility.HtmlEncode(sede);
                string costoTexto = costo.ToString("0.00", CultureInfo.InvariantCulture);

                string mensajeHtml = $@"
                <div style='font-family: Arial, sans-serif; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #ddd; border-radius: 8px; overflow: hidden;'>
                    <div style='text-align: center; background-color: #ffffff; padding: 0;'>
                        <img src='http://201.144.103.221/citas/images/Sin_titulo.png' alt='Gobierno del Estado SLP' style='width: 100%; height: auto;' />
                    </div>
                    <div style='padding: 30px 20px;'>
                        <h2 style='color: #055A1C; text-align: center; margin-top: 0;'>{tituloPrincipal}</h2>
                        <p style='font-size: 15px; margin-top: 20px;'>Estimado/a <b>{identificadorHtml}</b>,</p>
                        <p style='font-size: 15px;'>{textoSecundario} A continuación, le presentamos los detalles:</p>

                        <div style='background-color: #f9f9f9; padding: 20px; border-radius: 6px; border-left: 5px solid #055A1C; margin: 25px 0;'>
                            <p style='margin: 0 0 10px 0; font-size: 15px;'><b>Trámite:</b> {tramiteHtml}</p>
                            <p style='margin: 0 0 10px 0; font-size: 15px;'><b>Costo del Servicio:</b> <span style='color: #055A1C; font-weight: bold;'>${costoTexto}</span></p>
                            <p style='margin: 0 0 10px 0; font-size: 15px;'><b>Nueva Fecha y Hora:</b> <span style='color: #E60064; font-weight: bold;'>{horaCitaMexico.ToString("dd/MM/yyyy HH:mm")} hrs</span></p>
                            <p style='margin: 0 0 15px 0; font-size: 15px;'><b>Sede:</b> {sedeHtml}</p>
                            <h3 style='margin: 0; color: #055A1C; font-size: 20px;'>FOLIO: {folioHtml}</h3>
                        </div>

                        <h4 style='color: #055A1C; margin-top: 30px; margin-bottom: 10px; font-size: 16px;'>📋 REQUISITOS OBLIGATORIOS</h4>
                        <div style='background-color: #fff9e6; padding: 15px 20px; border: 1px dashed #ffc107; border-radius: 6px;'>
                            <ul style='color: #555; line-height: 1.5; font-size: 14px; margin: 0; padding-left: 20px;'>
                                {listaRequisitosHtml}
                            </ul>
                        </div>

                        <h4 style='color: #E60064; margin-top: 30px; margin-bottom: 10px; font-size: 16px;'>⚠️ AVISOS IMPORTANTES Y PENALIZACIÓN</h4>
                        <ul style='color: #555; line-height: 1.6; padding-left: 20px; font-size: 14px; margin-top: 0;'>
                            <li><strong>El trámite es estrictamente personal.</strong> Es obligatorio presentar Identificación Oficial (ID) vigente.</li>
                            <li><strong>SISTEMA DE PENALIZACIÓN:</strong> Si usted agenda su cita y NO asiste, el sistema lo bloqueará automáticamente, impidiéndole agendar un nuevo trámite durante <strong>1 semana</strong>.</li>
                        </ul>

                        <hr style='border: 0; border-top: 1px solid #eee; margin: 30px 0;' />
                        <p style='font-size: 11px; color: #999; text-align: center; margin: 0;'>Por favor, <strong>NO conteste este correo.</strong> Las respuestas a esta dirección no son monitoreadas.</p>
                    </div>
                </div>";

                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
                request.Headers.Add("api-key", apiKey);

                var payload = new
                {
                    sender = new { name = "Registro Civil Citas SLP", email = correoOrigen },
                    to = new[] { new { email = correoDestino.Trim(), name = identificador } },
                    subject = $"{tituloPrincipal} - Folio: {folio}",
                    htmlContent = mensajeHtml
                };

                string jsonContent = JsonSerializer.Serialize(payload);
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                using var httpClient = _httpClientFactory != null ? _httpClientFactory.CreateClient() : new HttpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                HttpResponseMessage response = await httpClient.SendAsync(request, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    string errorResponse = await response.Content.ReadAsStringAsync();
                    _logger?.LogError("Brevo rechazó el envío. Código: {Status}. Detalle: {Detalle}", (int)response.StatusCode, errorResponse);
                    return;
                }

                _logger?.LogInformation("Correo enviado exitosamente a {Destino} mediante API de Brevo.", correoDestino);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error inesperado al enviar el correo por API de Brevo.");
            }
        }

        public async Task EnviarCorreoCancelacionAsync(
            string? correoDestino,
            string identificador,
            string folio,
            DateTime fechaHora,
            string tramite,
            string sede)
        {
            if (string.IsNullOrWhiteSpace(correoDestino))
            {
                _logger?.LogWarning("No se envió el correo de cancelación: el correo destinatario está vacío.");
                return;
            }

            string apiKey = _config["EmailSettings:ApiKey"] ?? _config["EmailSettings:PasswordApp"] ?? string.Empty;
            string correoOrigen = _config["EmailSettings:Correo"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(correoOrigen))
            {
                _logger?.LogError("No se envió el correo de cancelación: falta configurar EmailSettings:ApiKey o EmailSettings:Correo.");
                return;
            }

            try
            {
                DateTime horaCitaMexico = fechaHora.Kind == DateTimeKind.Utc ? fechaHora.AddHours(-6) : fechaHora;
                string identificadorHtml = WebUtility.HtmlEncode(identificador);
                string folioHtml = WebUtility.HtmlEncode(folio);
                string tramiteHtml = WebUtility.HtmlEncode(tramite);
                string sedeHtml = WebUtility.HtmlEncode(sede);
                string fechaHtml = WebUtility.HtmlEncode(horaCitaMexico.ToString("dd/MM/yyyy HH:mm"));
                string mensajeHtml = $@"
                <div style='font-family: Arial, sans-serif; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #ddd; border-radius: 8px; padding: 30px 20px;'>
                    <h2 style='color: #055A1C; text-align: center;'>Cita cancelada</h2>
                    <p>Estimado/a <b>{identificadorHtml}</b>,</p>
                    <p>Le confirmamos que su cita fue cancelada y el horario quedó liberado.</p>
                    <div style='background-color: #f9f9f9; padding: 20px; border-left: 5px solid #055A1C;'>
                        <p><b>Folio:</b> {folioHtml}</p>
                        <p><b>Trámite:</b> {tramiteHtml}</p>
                        <p><b>Fecha y hora:</b> {fechaHtml} hrs</p>
                        <p><b>Sede:</b> {sedeHtml}</p>
                    </div>
                    <p>Si necesita realizar el trámite, puede solicitar una nueva cita desde el portal.</p>
                </div>";

                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
                request.Headers.Add("api-key", apiKey);
                var payload = new
                {
                    sender = new { name = "Registro Civil Citas SLP", email = correoOrigen },
                    to = new[] { new { email = correoDestino.Trim(), name = identificador } },
                    subject = $"Cancelación de cita - Folio: {folio}",
                    htmlContent = mensajeHtml
                };
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var httpClient = _httpClientFactory != null ? _httpClientFactory.CreateClient() : new HttpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using HttpResponseMessage response = await httpClient.SendAsync(request, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    string errorResponse = await response.Content.ReadAsStringAsync();
                    _logger?.LogError("Brevo rechazó el correo de cancelación. Código: {Status}. Detalle: {Detalle}", (int)response.StatusCode, errorResponse);
                    return;
                }

                _logger?.LogInformation("Correo de cancelación enviado exitosamente a {Destino}.", correoDestino);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error inesperado al enviar el correo de cancelación.");
            }
        }

        public async Task EnviarAvisoCierreInhabilAsync(
            string? correoDestino,
            string identificador,
            string folio,
            DateTime fechaHora,
            string tramite,
            string sede,
            DateTime fechaLimite)
        {
            if (string.IsNullOrWhiteSpace(correoDestino))
            {
                _logger?.LogWarning("No se envió el aviso de cierre: el correo destinatario está vacío.");
                return;
            }

            string apiKey = _config["EmailSettings:ApiKey"] ?? _config["EmailSettings:PasswordApp"] ?? string.Empty;
            string correoOrigen = _config["EmailSettings:Correo"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(correoOrigen))
            {
                _logger?.LogError("No se envió el aviso de cierre: falta configurar EmailSettings:ApiKey/PasswordApp o EmailSettings:Correo.");
                return;
            }

            try
            {
                DateTime horaCitaMexico = fechaHora.Kind == DateTimeKind.Utc ? fechaHora.AddHours(-6) : fechaHora;
                string nombreHtml = WebUtility.HtmlEncode(identificador);
                string folioHtml = WebUtility.HtmlEncode(folio);
                string tramiteHtml = WebUtility.HtmlEncode(tramite);
                string sedeHtml = WebUtility.HtmlEncode(sede);
                string fechaHtml = WebUtility.HtmlEncode(horaCitaMexico.ToString("dd/MM/yyyy HH:mm"));
                string limiteHtml = WebUtility.HtmlEncode(fechaLimite.ToString("dd/MM/yyyy"));
                string mensajeHtml = $@"
                <div style='font-family: Arial, sans-serif; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #ddd; border-radius: 8px; padding: 30px 20px;'>
                    <h2 style='color: #055A1C; text-align: center;'>Aviso importante sobre su cita</h2>
                    <p>Estimado/a <b>{nombreHtml}</b>,</p>
                    <p>Por el cierre de la sede en la fecha de su cita, su cita fue cancelada administrativamente. Esta cancelación no genera sanción.</p>
                    <div style='background-color: #f9f9f9; padding: 20px; border-left: 5px solid #055A1C;'>
                        <p><b>Folio:</b> {folioHtml}</p>
                        <p><b>Trámite:</b> {tramiteHtml}</p>
                        <p><b>Fecha y hora original:</b> {fechaHtml} hrs</p>
                        <p><b>Sede:</b> {sedeHtml}</p>
                        <p><b>Plazo para resolverlo:</b> hasta el {limiteHtml} (7 días hábiles).</p>
                    </div>
                    <p>Dentro de ese plazo puede solicitar otra fecha, cancelar la cita o acudir personalmente a la sede indicada. Si no encuentra una fecha disponible, se respetará su turno en esa sede durante el plazo señalado. Consulte su cita en el portal con su folio.</p>
                    <p>La cita cancelada por el cierre no se considera inasistencia y no genera penalización.</p>
                </div>";

                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
                request.Headers.Add("api-key", apiKey);
                var payload = new
                {
                    sender = new { name = "Registro Civil Citas SLP", email = correoOrigen },
                    to = new[] { new { email = correoDestino.Trim(), name = identificador } },
                    subject = $"Aviso de cierre y cambio de cita - Folio: {folio}",
                    htmlContent = mensajeHtml
                };
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var httpClient = _httpClientFactory != null ? _httpClientFactory.CreateClient() : new HttpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using HttpResponseMessage response = await httpClient.SendAsync(request, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    string errorResponse = await response.Content.ReadAsStringAsync();
                    _logger?.LogError("Brevo rechazó el aviso de cierre. Código: {Status}. Detalle: {Detalle}", (int)response.StatusCode, errorResponse);
                    return;
                }

                _logger?.LogInformation("Aviso de cierre enviado correctamente por correo.");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error al enviar el aviso de cierre por correo.");
            }
        }    }
}
