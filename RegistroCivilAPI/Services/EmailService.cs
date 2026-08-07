using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace RegistroCivilAPI.Services
{
    // Esta es la "promesa" de lo que el servicio puede hacer
    public interface IEmailService
    {
        Task EnviarCorreoConfirmacionAsync(string correoDestino, string identificador, string folio, DateTime fechaHora, string tramite, decimal costo, string sede, string requisitos, bool esReagendada = false);
    }

    // Esta es la implementación real
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task EnviarCorreoConfirmacionAsync(string correoDestino, string identificador, string folio, DateTime fechaHora, string tramite, decimal costo, string sede, string requisitos, bool esReagendada = false)
        {
            try
            {
                string correoOrigen = _config["EmailSettings:Correo"];
                string passwordApp = _config["EmailSettings:PasswordApp"];

                if (string.IsNullOrEmpty(correoOrigen) || string.IsNullOrEmpty(passwordApp)) return;

                var smtpClient = new SmtpClient("smtp.gmail.com")
                {
                    Port = 587,
                    Credentials = new NetworkCredential(correoOrigen, passwordApp),
                    EnableSsl = true,
                };

                string listaRequisitosHtml = "";
                if (!string.IsNullOrWhiteSpace(requisitos))
                {
                    var lineas = requisitos.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var linea in lineas) { listaRequisitosHtml += $"<li style='margin-bottom: 8px;'>{linea.Trim('•', ' ', '-')}</li>"; }
                }

                string tituloPrincipal = esReagendada ? "Confirmación de Cita Reagendada" : "Confirmación de Cita Registrada";
                string textoSecundario = esReagendada ? "Su cita ha sido reagendada exitosamente para una nueva fecha." : "Su cita ha sido generada exitosamente.";

                var mensajeHtml = $@"
                <div style='font-family: Arial, sans-serif; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #ddd; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 8px rgba(0,0,0,0.1);'>
                    <div style='text-align: center; background-color: #ffffff; padding: 0;'>
                        <img src='http://201.144.103.221/citas/images/Sin_titulo.png' alt='Gobierno del Estado SLP' style='width: 100%; height: auto;' />
                    </div>
                    <div style='padding: 30px 20px;'>
                        <h2 style='color: #055A1C; text-align: center; margin-top: 0;'>{tituloPrincipal}</h2>
                        <p style='font-size: 15px; margin-top: 20px;'>Estimado/a <b>{identificador}</b>,</p>
                        <p style='font-size: 15px;'>{textoSecundario} A continuación, le presentamos los detalles:</p>
                        
                        <div style='background-color: #f9f9f9; padding: 20px; border-radius: 6px; border-left: 5px solid #055A1C; margin: 25px 0;'>
                            <p style='margin: 0 0 10px 0; font-size: 15px;'><b>Trámite:</b> {tramite}</p>
                            <p style='margin: 0 0 10px 0; font-size: 15px;'><b>Costo del Servicio:</b> <span style='color: #055A1C; font-weight: bold;'>${costo.ToString("0.00")}</span></p>
                            <p style='margin: 0 0 10px 0; font-size: 15px;'><b>Nueva Fecha y Hora:</b> <span style='color: #E60064; font-weight: bold;'>{fechaHora.ToString("dd/MM/yyyy HH:mm")} hrs</span></p>
                            <p style='margin: 0 0 15px 0; font-size: 15px;'><b>Sede:</b> {sede}</p>
                            <h3 style='margin: 0; color: #055A1C; font-size: 20px;'>FOLIO: {folio}</h3>
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

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(correoOrigen, "Registro Civil Citas"),
                    Subject = $"{tituloPrincipal} - Folio: {folio}",
                    Body = mensajeHtml,
                    IsBodyHtml = true,
                };
                mailMessage.To.Add(correoDestino);
                await smtpClient.SendMailAsync(mailMessage);
            }
            catch (System.Exception ex) { System.Console.WriteLine("ERROR AL ENVIAR CORREO: " + ex.Message); }
        }
    }
}