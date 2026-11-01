namespace Millet.Compartido.Infrastructure.Banxico;

/// <summary>
/// K10.6: consulta del SIE de Banxico. El token de Millet vive en Key Vault
/// (<c>IntegracionesBanxico--Token</c>); en local, en user-secrets. Nunca en appsettings.
/// Sin token el worker no corre y lo dice al arrancar.
/// </summary>
public sealed class BanxicoOptions
{
    public const string SectionName = "IntegracionesBanxico";

    public string? Token { get; set; }
    public string BaseUrl { get; set; } = "https://www.banxico.org.mx/SieAPIRest/service/v1/";
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
    public bool Disabled { get; set; }

    public bool TieneToken => !string.IsNullOrWhiteSpace(Token);
}
