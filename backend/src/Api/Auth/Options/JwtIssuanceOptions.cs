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

    /// <summary>
    /// Vida del access token en minutos. Default 15 (86e3x819w): si alguien
    /// copia el token (XSS o equipo compartido) sólo le sirve unos minutos.
    /// El frontend lo renueva en silencio antes de que venza
    /// (<c>lib/auth/renovacion-sesion.ts</c>), así que acortarlo no corta
    /// formularios largos. Antes era 60 (ADR-0007 PR 2).
    /// </summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 15;

    /// <summary>Límite inferior aceptado al arranque: menos obliga a renovar casi en cada llamada.</summary>
    public const int VidaMinimaMinutos = 5;

    /// <summary>Límite superior aceptado al arranque: más alarga la ventana de un token robado.</summary>
    public const int VidaMaximaMinutos = 60;
}
