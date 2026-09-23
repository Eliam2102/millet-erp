namespace Millet.Identidad.Infrastructure.Adapters;

/// <summary>Credenciales de la aplicación de directorio; se inyectan desde secretos, nunca del repositorio.</summary>
public sealed class EntraDirectoryOptions
{
    public const string SectionName = "Identidad:EntraDirectory";
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public bool AllowDevPlaceholder { get; set; }
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);
}
