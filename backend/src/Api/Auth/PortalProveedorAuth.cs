using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Millet.Api.Auth.Options;
using Millet.CuentasPorPagar.Domain.Ports.Portal;
using Millet.SharedKernel.Application;

namespace Millet.Api.Auth;

/// <summary>
/// Esquema de autenticación propio del portal de proveedores (P8.3). Token con audiencia
/// <see cref="Audience"/> y sin permisos del ERP: el esquema por defecto del ERP lo rechaza
/// (audiencia distinta) y este esquema rechaza los tokens del ERP por la misma razón.
/// </summary>
public static class PortalProveedorAuth
{
    public const string Scheme = "PortalProveedor";
    public const string Policy = "portal-proveedor";
    public const string Audience = "millet-portal-proveedores";
    public const string ProveedorIdClaim = "proveedor_id";
    public const string EmpresaIdClaim = "empresa_id";
    public static readonly TimeSpan Vigencia = TimeSpan.FromHours(8);

    public static IServiceCollection AddPortalProveedorAuth(
        this IServiceCollection services, JwtIssuanceOptions jwt, SecurityKey signingKey)
    {
        services.AddAuthentication().AddJwtBearer(Scheme, options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = MilletClaimTypes.Email,
            };
        });
        services.AddAuthorizationBuilder().AddPolicy(Policy, p => p
            .AddAuthenticationSchemes(Scheme)
            .RequireAuthenticatedUser()
            .RequireClaim(ProveedorIdClaim)
            .RequireClaim(EmpresaIdClaim));
        services.AddScoped<IPortalTokenEmisor, PortalProveedorTokenService>();
        return services;
    }

    public static Guid? ProveedorId(this System.Security.Claims.ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(ProveedorIdClaim)?.Value, out var id) ? id : null;
}

public sealed class PortalProveedorTokenService(IOptions<JwtIssuanceOptions> options, IClock clock) : IPortalTokenEmisor
{
    public PortalToken Emitir(Guid proveedorId, Guid empresaId, string email)
    {
        var opts = options.Value;
        var now = clock.UtcNow;
        var expira = now + PortalProveedorAuth.Vigencia;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = opts.Issuer,
            Audience = PortalProveedorAuth.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expira.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [MilletClaimTypes.Sub] = email,
                [MilletClaimTypes.Email] = email,
                [PortalProveedorAuth.ProveedorIdClaim] = proveedorId.ToString(),
                [PortalProveedorAuth.EmpresaIdClaim] = empresaId.ToString(),
                // Lo lee CurrentEmpresaContext para el filtro de empresa de los DbContext.
                [MilletClaimTypes.CurrentEmpresaId] = empresaId.ToString(),
                [MilletClaimTypes.AuthType] = "portal_proveedor",
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SigningKey)), SecurityAlgorithms.HmacSha256),
        };
        return new PortalToken(new JsonWebTokenHandler().CreateToken(descriptor), expira);
    }
}
