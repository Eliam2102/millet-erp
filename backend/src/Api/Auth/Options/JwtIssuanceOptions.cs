namespace Millet.Api.Auth.Options;

/// <summary>
/// Configuración para emitir y validar el JWT del API. Se bindea desde la
/// sección <c>Auth:Jwt</c>. <see cref="SigningKey"/> NO debe vivir en
/// archivos de configuración versionados — viene de Key Vault en QA/Prod
/// y de <c>appsettings.Development.json</c> en dev local (donde es público
/// por diseño porque el modo Development jamás llega a un entorno real).
/// </summary>
public sealed class JwtIssuanceOptions
{
    /// <summary>Issuer del JWT (claim <c>iss</c>). Default <c>millet-erp-api</c>.</summary>
    public string Issuer { get; set; } = "millet-erp-api";

    /// <summary>Audience esperada (claim <c>aud</c>). Default <c>millet-erp-api</c>.</summary>
    public string Audience { get; set; } = "millet-erp-api";

    /// <summary>
    /// Clave simétrica usada para firmar (HMAC SHA-256) y validar el JWT.
    /// Mínimo 32 bytes (256 bits). Requerido al arranque vía
    /// <c>ValidateOnStart()</c>.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Vida del access token en minutos. Default 60 (ADR-0007 PR 2 spec).</summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 60;
}
