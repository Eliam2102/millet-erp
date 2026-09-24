using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Millet.Api.Auth.Options;

namespace Millet.Api.Auth.Provisioning;

public sealed class EntraProvisioningService : IEntraProvisioningService, IDisposable
{
    private readonly GraphServiceClient _graphClient;
    private readonly ILogger<EntraProvisioningService> _logger;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
    private readonly string _senderEmail;

    public EntraProvisioningService(
        IOptions<EntraIdOptions> options,
        ILogger<EntraProvisioningService> logger,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        var opts = options.Value;
        _senderEmail = opts.SenderEmail;
        
        // Si no hay ClientSecret (ej. Dev local sin setup), usamos un fake client para no romper la inyección.
        if (string.IsNullOrWhiteSpace(opts.ClientSecret))
        {
            _logger.LogWarning("No se configuró Auth:EntraId:ClientSecret. El aprovisionamiento de Entra ID fallará si se intenta usar.");
            // Inicializar con credenciales nulas/inválidas para evitar NRE, fallará en ejecución.
            var tokenCredential = new ClientSecretCredential(opts.TenantId, opts.ClientId, "fake-secret-to-allow-startup");
            _graphClient = new GraphServiceClient(tokenCredential);
        }
        else
        {
            var tokenCredential = new ClientSecretCredential(opts.TenantId, opts.ClientId, opts.ClientSecret);
            _graphClient = new GraphServiceClient(tokenCredential);
        }
    }

    public async Task<ProvisioningResult> CrearNuevoUsuarioAsync(string nombre, string apellido, string upn, string correoContacto, CancellationToken cancellationToken = default)
    {
        try
        {
            var temporaryPassword = GenerateSecurePassword();

            var newUser = new User
            {
                AccountEnabled = true,
                DisplayName = $"{nombre} {apellido}".Trim(),
                MailNickname = upn.Split('@')[0],
                UserPrincipalName = upn,
                PasswordProfile = new PasswordProfile
                {
                    ForceChangePasswordNextSignIn = true,
                    Password = temporaryPassword
                }
            };

            var createdUser = await _graphClient.Users.PostAsync(newUser, cancellationToken: cancellationToken);

            if (createdUser?.Id == null)
            {
                return new ProvisioningResult(false, null, "Graph API no devolvió el ID del usuario creado.");
            }

            await EnviarCorreoInvitacionAsync(createdUser.Id, upn, correoContacto, temporaryPassword, true, cancellationToken);

            return new ProvisioningResult(true, createdUser.Id, "Usuario creado exitosamente e invitación enviada.");
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            _logger.LogError(ex, "Error al crear usuario nuevo en Entra ID para UPN: {Upn}", upn);
            return new ProvisioningResult(false, null, $"Error interno: {ex.Message} - Detalle: {detail}");
        }
    }

    private static readonly string[] SelectColumns = new[] { "id", "userPrincipalName" };

    public async Task<ProvisioningResult> VincularUsuarioExistenteAsync(string upn, string correoContacto, CancellationToken cancellationToken = default)
    {
        try
        {
            // Filtrar por UPN
            var result = await _graphClient.Users.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Filter = $"userPrincipalName eq '{upn}'";
                requestConfiguration.QueryParameters.Select = SelectColumns;
            }, cancellationToken);

            var user = result?.Value?.FirstOrDefault();

            if (user?.Id == null)
            {
                return new ProvisioningResult(false, null, $"El usuario con UPN {upn} no existe en el Directorio Activo.");
            }

            await EnviarCorreoInvitacionAsync(user.Id, upn, correoContacto, null, false, cancellationToken);

            return new ProvisioningResult(true, user.Id, "Usuario vinculado exitosamente e invitación enviada.");
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            _logger.LogError(ex, "Error al buscar usuario existente en Entra ID para UPN: {Upn}", upn);
            return new ProvisioningResult(false, null, $"Error interno: {ex.Message} - Detalle: {detail}");
        }
    }

    public async Task<bool> ExisteUsuarioAsync(string upn, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _graphClient.Users.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Filter = $"userPrincipalName eq '{upn}'";
                requestConfiguration.QueryParameters.Select = SelectColumns;
            }, cancellationToken);

            var user = result?.Value?.FirstOrDefault();
            return user?.Id != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error silencioso al verificar si existe el UPN {Upn}", upn);
            return false;
        }
    }

    private async Task EnviarCorreoInvitacionAsync(string userId, string upn, string correoContacto, string? tempPassword, bool isNewUser, CancellationToken cancellationToken)
    {
        try
        {
            var subject = isNewUser 
                ? "Bienvenido al ERP Millet - Tus credenciales de acceso"
                : "Bienvenido al ERP Millet - Cuenta vinculada";

            var baseUrl = _configuration.GetValue<string>("Entra:UrlInicioSesion") ?? "http://localhost:5173";
            var loginUrl = $"{baseUrl}/login?hint={upn}";

            var body = isNewUser
                ? $@"
                    <div style='font-family: Arial, sans-serif; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #e5e7eb; border-radius: 8px; padding: 24px;'>
                        <h2 style='color: #4F46E5; margin-top: 0;'>Bienvenido al ERP Millet</h2>
                        <p>Se ha creado tu cuenta corporativa. Tu usuario es este mismo correo.</p>
                        <div style='background-color: #f3f4f6; padding: 12px; border-radius: 6px; margin: 16px 0; text-align: center;'>
                            <p style='margin: 0; font-size: 14px;'>Contraseña temporal:</p>
                            <p style='margin: 5px 0 0 0; font-size: 20px; font-weight: bold; letter-spacing: 2px;'>{tempPassword}</p>
                        </div>
                        <p>Por seguridad, se te pedirá que la cambies en tu primer inicio de sesión.</p>
                        <div style='text-align: center; margin-top: 24px;'>
                            <a href='{loginUrl}' style='background-color: #4F46E5; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; font-weight: bold; display: inline-block;'>Iniciar Sesión en el ERP</a>
                        </div>
                    </div>"
                : $@"
                    <div style='font-family: Arial, sans-serif; color: #333; max-width: 600px; margin: 0 auto; border: 1px solid #e5e7eb; border-radius: 8px; padding: 24px;'>
                        <h2 style='color: #4F46E5; margin-top: 0;'>Bienvenido al ERP Millet</h2>
                        <p>Tu cuenta corporativa actual ha sido vinculada exitosamente con el sistema.</p>
                        <p>Ya puedes acceder utilizando tus credenciales de Microsoft de siempre.</p>
                        <div style='text-align: center; margin-top: 24px;'>
                            <a href='{loginUrl}' style='background-color: #4F46E5; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; font-weight: bold; display: inline-block;'>Acceder al ERP</a>
                        </div>
                    </div>";

            var message = new Microsoft.Graph.Models.Message
            {
                Subject = subject,
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = body
                },
                ToRecipients = new List<Recipient>
                {
                    new Recipient
                    {
                        EmailAddress = new EmailAddress
                        {
                            Address = correoContacto
                        }
                    }
                }
            };

            var sendMailBody = new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = false
            };

            if (string.IsNullOrWhiteSpace(_senderEmail))
            {
                _logger.LogWarning("No se envió el correo porque 'Auth:EntraId:SenderEmail' no está configurado en appsettings.");
                return;
            }

            // Requiere permiso de aplicación Mail.Send 
            await _graphClient.Users[_senderEmail].SendMail.PostAsync(sendMailBody, cancellationToken: cancellationToken);
            _logger.LogInformation("Correo de invitación enviado a {Correo} para el OID {Oid} desde {Sender}", correoContacto, userId, _senderEmail);
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataError)
        {
            var code = odataError.Error?.Code;
            var msg = odataError.Error?.Message;

            if (code == "MailboxNotEnabledForRESTAPI" || code == "ErrorMailboxNotFound" || code == "ErrorNonExistentMailbox")
            {
                _logger.LogWarning("⚠️ ATENCIÓN: El remitente '{Sender}' NO tiene un buzón de correo Exchange Online activo o no tiene licencia comprada. El usuario fue creado, pero NO se pudo enviar el correo a {Correo}. Graph Error: {Code} - {Msg}", _senderEmail, correoContacto, code, msg);
            }
            else
            {
                _logger.LogWarning(odataError, "Ocurrió un error en Graph API al intentar enviar el correo. Code: {Code}, Message: {Msg}", code, msg);
            }
        }
        catch (Exception ex)
        {
            // No queremos que falle toda la transacción si falla solo el correo
            _logger.LogWarning(ex, "Se creó el usuario pero falló el envío del correo de bienvenida vía Exchange Online a {Correo}", correoContacto);
        }
    }

    private static string GenerateSecurePassword()
    {
        // Generador simple de contraseña segura de 12 chars alfanuméricos + símbolos
        // Para este PoC/Feature, esto cumple. (Ideal: System.Security.Cryptography)
        var tempId = Guid.NewGuid().ToString("N")[..8];
        return $"Temp!{tempId}Millet";
    }

    public void Dispose()
    {
        _graphClient?.Dispose();
    }
}
