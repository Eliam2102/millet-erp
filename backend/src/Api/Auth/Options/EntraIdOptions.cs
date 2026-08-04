namespace Millet.Api.Auth.Options;

/// <summary>
/// Configuración para validar tokens emitidos por Microsoft Entra ID que
/// el frontend envía a <c>POST /api/auth/sesion</c>. Se bindea desde la
/// sección <c>Auth:EntraId</c>. Solo se usa en
/// <c>AuthMode.EntraId</c>; en <c>FakeForLocalDev</c> el flujo se salta
/// vía <c>POST /api/dev/fake-login</c> (ver ADR-0015).
/// </summary>
public sealed class EntraIdOptions
{
    /// <summary>Tenant ID de Entra (GUID en string). Vive en KV en QA/Prod.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Client ID del API app registration (GUID en string).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Audience esperada del access token. Generalmente
    /// <c>api://{ClientId}</c> o el AppIdUri configurado en el app
    /// registration.
    /// </summary>
    public string Audience { get; set; } = string.Empty;
}
