using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Millet.Api.Auth.Options;

namespace Millet.Api.Auth;

/// <summary>
/// Datos extraídos de un token de Entra validado: el oid del usuario
/// (identificador estable cross-tenant) más email y nombre desde los claims
/// estándar OIDC. <see cref="Email"/> y <see cref="Name"/> son tolerantes a
/// nulls; auto-provisión rellena con valores razonables si vienen vacíos.
/// </summary>
public sealed record EntraTokenClaims(string Oid, string? Email, string? Name);

/// <summary>
/// Datos extraídos de un token de Entra validado que pertenece a un
/// service principal (PR A — Glass Agent y futuros M2M). Distinto de
/// <see cref="EntraTokenClaims"/> porque los SPs usan el claim
/// <c>appid</c> (Application ID) como identidad principal, no
/// <c>oid</c>. Ambos claims se exponen para que el resolver pueda
/// matchear y persistir.
/// </summary>
public sealed record ValidatedServicePrincipalToken(Guid AppId, Guid ObjectId);

/// <summary>
/// Valida tokens de acceso emitidos por Microsoft Entra ID. Cachea los
/// metadatos OIDC (issuer, JWKS) por hasta 24h vía
/// <see cref="ConfigurationManager{T}"/>; refresca automáticamente cuando
/// hay rotación de keys.
///
/// <para>
/// <see cref="ValidateAsync"/> se invoca desde <c>POST /api/auth/sesion</c>
/// para humanos (oid + upn).
/// <see cref="TryValidateServicePrincipalAsync"/> se invoca desde el
/// authentication scheme <c>EntraServicePrincipal</c> (PR A) cuando el
/// PolicyScheme detecta un token Entra en Authorization header de un
/// request normal — el SP NO hace exchange, va directo con el token Entra.
/// </para>
/// </summary>
public interface IEntraTokenValidator
{
    Task<EntraTokenClaims> ValidateAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida firma + iss + aud + exp del token Entra y, si se valida OK,
    /// determina si es de un service principal (claim <c>appid</c>
    /// presente, sin <c>upn</c>/<c>preferred_username</c>).
    ///
    /// <para>
    /// Retorna <c>null</c> si: (a) el token NO es de service principal
    /// (ej. es de usuario delegado que llegó por error). El handler debe
    /// emitir <c>NoResult</c> en ese caso para que el PolicyScheme
    /// forwardee al scheme JWT clásico.
    /// </para>
    ///
    /// <para>
    /// Lanza <see cref="UnauthorizedAccessException"/> si: (a) la firma
    /// del JWT es inválida, (b) el token está expirado, (c) iss/aud no
    /// matchean. El handler captura y emite <c>Fail</c> con el type
    /// apropiado.
    /// </para>
    /// </summary>
    Task<ValidatedServicePrincipalToken?> TryValidateServicePrincipalAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
}

public sealed class EntraTokenValidator : IEntraTokenValidator
{
    private readonly IOptions<EntraIdOptions> _options;
    private readonly Lazy<ConfigurationManager<OpenIdConnectConfiguration>> _configManager;
    private readonly JsonWebTokenHandler _handler = new();

    public EntraTokenValidator(IOptions<EntraIdOptions> options)
    {
        _options = options;
        _configManager = new Lazy<ConfigurationManager<OpenIdConnectConfiguration>>(BuildConfigManager);
    }

    public async Task<EntraTokenClaims> ValidateAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new UnauthorizedAccessException("Token de Entra vacío.");
        }

        var opts = _options.Value;
        var oidcConfig = await _configManager.Value.GetConfigurationAsync(cancellationToken);

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = new[]
            {
                $"https://login.microsoftonline.com/{opts.TenantId}/v2.0",
                $"https://sts.windows.net/{opts.TenantId}/",
            },
            ValidateAudience = true,
            ValidAudience = opts.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = oidcConfig.SigningKeys,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        var result = await _handler.ValidateTokenAsync(accessToken, validationParameters);
        if (!result.IsValid)
        {
            throw new UnauthorizedAccessException(
                $"Token de Entra inválido: {result.Exception?.Message ?? "razón desconocida"}.");
        }

        // Usamos FindFirst (que toma el primer claim del tipo) en lugar de
        // ToDictionary porque Entra emite claims duplicados legítimamente
        // (ej. 'amr' = ["pwd", "mfa"] cuando el user usó MFA). ToDictionary
        // revienta con ArgumentException en duplicados.
        var identity = result.ClaimsIdentity;

        // El oid es el identificador estable cross-tenant de Entra.
        var oid = identity.FindFirst("oid")?.Value
            ?? throw new UnauthorizedAccessException("Token de Entra sin claim 'oid'.");

        // Email y name son nice-to-have (algunos tokens los incluyen como
        // 'email', 'preferred_username', 'upn'; 'name', 'given_name', etc.).
        var email = identity.FindFirst("email")?.Value
            ?? identity.FindFirst("preferred_username")?.Value
            ?? identity.FindFirst("upn")?.Value;
        var name = identity.FindFirst("name")?.Value;

        return new EntraTokenClaims(oid, email, name);
    }

    /// <summary>
    /// PR A — service principal validation. Reusa el mismo
    /// <see cref="ConfigurationManager{T}"/> (singleton via Lazy) que
    /// <see cref="ValidateAsync"/> para no duplicar el cache de OIDC
    /// metadata (H1 del review del prompt PR A).
    /// </summary>
    public async Task<ValidatedServicePrincipalToken?> TryValidateServicePrincipalAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new UnauthorizedAccessException("Token de Entra vacío.");
        }

        var opts = _options.Value;
        var oidcConfig = await _configManager.Value.GetConfigurationAsync(cancellationToken);

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = new[]
            {
                $"https://login.microsoftonline.com/{opts.TenantId}/v2.0",
                $"https://sts.windows.net/{opts.TenantId}/",
            },
            ValidateAudience = true,
            ValidAudience = opts.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = oidcConfig.SigningKeys,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        var result = await _handler.ValidateTokenAsync(accessToken, validationParameters);
        if (!result.IsValid)
        {
            throw new UnauthorizedAccessException(
                $"Token de Entra inválido: {result.Exception?.Message ?? "razón desconocida"}.");
        }

        var identity = result.ClaimsIdentity;

        // Heurística de "es service principal":
        //   - claim 'appid' presente Y parseable como Guid
        //   - sin 'upn' ni 'preferred_username' (los delegados de usuario
        //     siempre traen al menos uno)
        // Si no es SP, retornamos null para que el handler emita NoResult
        // y el PolicyScheme forwardee al scheme Jwt (que va a fallar limpio
        // porque el token Entra no es API JWT, pero al menos no rompemos
        // el flow).
        var appIdClaim = identity.FindFirst("appid")?.Value;
        var upnClaim = identity.FindFirst("upn")?.Value
            ?? identity.FindFirst("preferred_username")?.Value;

        if (string.IsNullOrEmpty(appIdClaim) || !Guid.TryParse(appIdClaim, out var appId))
        {
            return null; // sin appid, no es SP
        }

        if (!string.IsNullOrEmpty(upnClaim))
        {
            return null; // tiene upn, es delegado de usuario, no SP
        }

        // oid del SP: identifica al service principal específico en el
        // tenant. Persiste como auditoría; en single-tenant no se usa
        // para resolución (D-RES del prompt PR A).
        var oidClaim = identity.FindFirst("oid")?.Value
            ?? throw new UnauthorizedAccessException("Token de service principal sin claim 'oid'.");
        if (!Guid.TryParse(oidClaim, out var oid))
        {
            throw new UnauthorizedAccessException($"Token de service principal con 'oid' no parseable: {oidClaim}.");
        }

        return new ValidatedServicePrincipalToken(appId, oid);
    }

    private ConfigurationManager<OpenIdConnectConfiguration> BuildConfigManager()
    {
        var opts = _options.Value;
        if (string.IsNullOrWhiteSpace(opts.TenantId))
        {
            throw new InvalidOperationException(
                "Auth:EntraId:TenantId no está configurado. Requerido para Auth:Mode=EntraId.");
        }

        var metadataUrl = $"https://login.microsoftonline.com/{opts.TenantId}/v2.0/.well-known/openid-configuration";
        return new ConfigurationManager<OpenIdConnectConfiguration>(
            metadataUrl,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true });
    }

}
