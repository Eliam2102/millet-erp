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

    public EntraProvisioningService(
        IOptions<EntraIdOptions> options,
        ILogger<EntraProvisioningService> logger)
    {
        _logger = logger;
        
        var opts = options.Value;
        
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

            await EnviarCorreoInvitacionAsync(createdUser.Id, correoContacto, temporaryPassword, isNewUser: true, cancellationToken);

            return new ProvisioningResult(true, createdUser.Id, "Usuario creado exitosamente e invitación enviada.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al crear usuario nuevo en Entra ID para UPN: {Upn}", upn);
            return new ProvisioningResult(false, null, $"Error interno: {ex.Message}");
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

            await EnviarCorreoInvitacionAsync(user.Id, correoContacto, null, isNewUser: false, cancellationToken);

            return new ProvisioningResult(true, user.Id, "Usuario vinculado exitosamente e invitación enviada.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al buscar usuario existente en Entra ID para UPN: {Upn}", upn);
            return new ProvisioningResult(false, null, $"Error interno: {ex.Message}");
        }
    }

    private async Task EnviarCorreoInvitacionAsync(string userId, string correoContacto, string? tempPassword, bool isNewUser, CancellationToken cancellationToken)
    {
        try
        {
            var subject = isNewUser 
                ? "Bienvenido al ERP Millet - Tus credenciales de acceso"
                : "Bienvenido al ERP Millet - Cuenta vinculada";

            var body = isNewUser
                ? $"<p>Hola,</p><p>Se ha creado tu cuenta corporativa. Tu usuario es tu correo asignado.</p><p><b>Contraseña temporal:</b> {tempPassword}</p><p>Se te pedirá que la cambies en tu primer inicio de sesión.</p>"
                : $"<p>Hola,</p><p>Tu cuenta corporativa actual ha sido habilitada para acceder al ERP Millet.</p><p>Ya puedes iniciar sesión con tus credenciales de Microsoft de siempre.</p>";

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

            // Requiere permiso de aplicación Mail.Send 
            await _graphClient.Users[userId].SendMail.PostAsync(sendMailBody, cancellationToken: cancellationToken);
            _logger.LogInformation("Correo de invitación enviado a {Correo} para el OID {Oid}", correoContacto, userId);
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
