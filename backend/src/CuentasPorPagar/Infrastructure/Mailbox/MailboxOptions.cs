namespace Millet.CuentasPorPagar.Infrastructure.Mailbox;

/// <summary>
/// Options del cliente Microsoft Graph para el mailbox dedicado de
/// CFDIs (F2-PR2, §4.1 del 04-cuidados-infra). Las 3 credenciales
/// (<see cref="TenantId"/>, <see cref="ClientId"/>,
/// <see cref="ClientSecret"/>) vienen de Key Vault en QA/Prod
/// (managed identity del App Service tiene <c>Get</c> sobre el secret).
/// Si alguna está vacía, el DI registra el stub <c>NoOpMailboxClient</c>
/// y el worker entra en idle.
/// </summary>
public sealed class MailboxOptions
{
    public const string SectionName = "CuentasPorPagar:Mailbox";

    /// <summary>Tenant ID de Microsoft Entra (Azure AD).</summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>App Registration Client ID con permiso Mail.ReadWrite sobre el mailbox.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Secret del App Registration. Solo en QA/Prod, nunca en dev local.</summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>
    /// UserPrincipalName del mailbox (ej. <c>cfdi@millet.com.mx</c>).
    /// El client lo usa como path en las llamadas Graph
    /// <c>/users/{upn}/...</c>.
    /// </summary>
    public string MailboxUpn { get; init; } = string.Empty;

    /// <summary>
    /// Mailbox a apuntar por la operación de ingestión — folder estándar
    /// <c>Inbox</c> por defecto, configurable si el área prefiere un
    /// folder dedicado.
    /// </summary>
    public string InboxFolderName { get; init; } = "Inbox";

    public string ProcessedFolderName { get; init; } = "Processed";
    public string FailedFolderName { get; init; } = "Failed";

    public int MaxMensajesPorTick { get; init; } = 25;
    public int TimeoutSecondsGraph { get; init; } = 30;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret) &&
        !string.IsNullOrWhiteSpace(MailboxUpn);
}
