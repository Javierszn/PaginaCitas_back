using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RegistroCivilAPI.Services
{
    public interface IEmailService
    {
        Task EnviarCorreoConfirmacionAsync(
            string correoDestino,
            string identificador,
            string folio,
            DateTime fechaHora,
            string tramite,
            decimal costo,
            string sede,
            string requisitos,
            bool esReagendada = false);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<EmailService> _logger;

        public EmailService(
            IConfiguration config,
            IHttpClientFactory httpClientFactory,
            ILogger<EmailService> logger)
        {
            _config = config;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task EnviarCorreoConfirmacionAsync(
            string correoDestino,
            string identificador,
            string folio,
            DateTime fechaHora,
            string tramite,
            decimal costo,
            string sede,
            string requisitos,
            bool esReagendada = false)
        {
            // Se conserva el nombre PasswordApp para no cambiar tus variables existentes.
            // El valor debe ser una API key de Brevo, no la contraseña de Gmail.
            string apiKey = _config["EmailSettings:PasswordApp"] ?? string.Empty;
            string correoOrigen = _config["EmailSettings:Correo"] ?? string.Empty;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogError(
                    "No se envió el correo: falta configurar EmailSettings:PasswordApp.");
                return;
            }

            if (string.IsNullOrWhiteSpace(correoOrigen))
            {
                _logger.LogError(
                    "No se envió el correo: falta configurar EmailSettings:Correo.");
                return;
            }

            if (string.IsNullOrWhiteSpace(correoDestino))
            {
                _logger.LogError(
                    "No se envió el correo: el correo destinatario está vacío.");
                return;
            }

            try
            {
                string listaRequisitosHtml = string.Empty;

                if (!string.IsNullOrWhiteSpace(requisitos))
                {
                    string[] lineas = requisitos.Split(
                        new[] { '\n', '\r' },
                        StringSplitOptions.RemoveEmptyEntries);

                    listaRequisitosHtml = string.Join(
                        Environment.NewLine,
                        lineas.Select(linea =>
                            $"<li style='margin-bottom: 8px;'>{WebUtility.HtmlEncode(linea.Trim('•', ' ', '-'))}</li>"));
                }

                string tituloPrincipal = esReagendada
                    ? "Confirmación de Cita Reagendada"
                    : "Confirmación de Cita Registrada";

                string textoSecundario = esReagendada
                    ? "Su cita ha sido reagendada exitosamente para una nueva fecha."
                    : "Su cita ha sido generada exitosamente.";

                // Convierte las fechas UTC a la hora de México (UTC-6).
                DateTime horaCitaMexico = fechaHora.Kind == DateTimeKind.Utc
                    ? fechaHora.AddHours(-6)
                    : fechaHora;

                string identificadorHtml = WebUtility.HtmlEncode(identificador);
                string folioHtml = WebUtility.HtmlEncode(folio);
                string tramiteHtml = WebUtility.HtmlEncode(tramite);
                string sedeHtml = WebUtility.HtmlEncode(sede);
                string costoTexto = costo.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture);

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

                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://api.brevo.com/v3/smtp/email");

                request.Headers.TryAddWithoutValidation("api-key", apiKey);

                request.Content = JsonContent.Create(new
                {
                    sender = new
                    {
                        name = "Registro Civil Citas",
                        email = correoOrigen
                    },
                    to = new[]
                    {
                        new { email = correoDestino }
                    },
                    subject = $"{tituloPrincipal} - Folio: {folio}",
                    htmlContent = mensajeHtml
                });

                using var httpClient = _httpClientFactory.CreateClient();
                using var timeoutCts =
                    new CancellationTokenSource(TimeSpan.FromSeconds(20));

                using HttpResponseMessage response = await httpClient.SendAsync(
                    request,
                    timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    string responseBody =
                        await response.Content.ReadAsStringAsync(timeoutCts.Token);

                    _logger.LogError(
                        "Brevo rechazó el envío. Estado HTTP: {StatusCode}. Respuesta: {ResponseBody}",
                        (int)response.StatusCode,
                        responseBody);

                    return;
                }

                _logger.LogInformation(
                    "Brevo aceptó el correo para el folio {Folio}.",
                    folio);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogError(
                    ex,
                    "Se agotó el tiempo de espera al enviar el correo del folio {Folio}.",
                    folio);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(
                    ex,
                    "Falló la conexión HTTPS con Brevo para el folio {Folio}.",
                    folio);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error inesperado al enviar el correo del folio {Folio}.",
                    folio);
            }
        }
    }
}