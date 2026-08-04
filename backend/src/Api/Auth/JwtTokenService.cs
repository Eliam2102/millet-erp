using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Millet.Api.Auth.Options;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.Auth;

/// <summary>
/// Token de acceso emitido al cliente: el string del JWT más cuándo expira.
/// El frontend usa <see cref="ExpiresAt"/> para programar refresh silent
/// (vía MSAL silentTokenAcquisition, ADR-0007).
/// </summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>
/// Emite el JWT del API que el frontend usa en <c>Authorization: Bearer</c>
/// para todos los requests subsecuentes. El bearer middleware lo valida con
/// la misma <see cref="JwtIssuanceOptions.SigningKey"/> (HMAC SHA-256).
///
/// Layout de claims emitidos en este PR (PR 3 agregará <c>roleIds</c>):
/// <list type="bullet">
///   <item><c>sub</c> — userId (Guid)</item>
///   <item><c>email</c></item>
///   <item><c>name</c></item>
///   <item><c>current_empresa_id</c> — solo si el usuario tiene empresa seleccionada</item>
///   <item><c>iss</c>, <c>aud</c>, <c>exp</c>, <c>iat</c>, <c>jti</c> — estándar</item>
/// </list>
/// </summary>
public interface IJwtTokenService
{
    AccessToken CreateAccessToken(Usuario usuario, Guid? currentEmpresaId);
}

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly IOptions<JwtIssuanceOptions> _options;
    private readonly IClock _clock;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtTokenService(IOptions<JwtIssuanceOptions> options, IClock clock)
    {
        _options = options;
        _clock = clock;
    }

    public AccessToken CreateAccessToken(Usuario usuario, Guid? currentEmpresaId)
    {
        var opts = _options.Value;
        var now = _clock.UtcNow;
        var expiresAt = now.AddMinutes(opts.AccessTokenLifetimeMinutes);

        var keyBytes = Encoding.UTF8.GetBytes(opts.SigningKey);
        var signingKey = new SymmetricSecurityKey(keyBytes);
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var claims = new Dictionary<string, object>
        {
            [MilletClaimTypes.Sub] = usuario.Id.ToString(),
            [MilletClaimTypes.Email] = usuario.Email,
            [MilletClaimTypes.Name] = usuario.Nombre,
        };

        if (currentEmpresaId is not null)
        {
            claims[MilletClaimTypes.CurrentEmpresaId] = currentEmpresaId.Value.ToString();
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = opts.Issuer,
            Audience = opts.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = credentials,
        };

        var token = _handler.CreateToken(descriptor);
        return new AccessToken(token, expiresAt);
    }
}
