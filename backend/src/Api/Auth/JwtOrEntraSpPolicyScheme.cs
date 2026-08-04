using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Millet.Api.Auth.Options;

namespace Millet.Api.Auth;

/// <summary>
/// PolicyScheme que decide a qué authentication scheme forwardear cada
/// request: <c>Jwt</c> (API JWT clásico, HMAC) o
/// <c>EntraServicePrincipal</c> (token Entra directo de un SP M2M).
///
/// <para>
/// <b>Decisión por <c>iss</c> del token:</b>
/// <list type="bullet">
///   <item><c>iss</c> = <c>https://login.microsoftonline.com/{tenant}/v2.0</c>
///         o <c>https://sts.windows.net/{tenant}/</c> → forward a
///         <c>EntraServicePrincipal</c>.</item>
///   <item>Cualquier otro <c>iss</c> (incluyendo <c>millet-erp-api</c> que
///         es el del API JWT) → forward a <c>Jwt</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Tolerancia a tokens malformados (H2 del review del prompt PR A):</b>
/// si el header Authorization no existe, no es Bearer, o el token NO es
/// un JWT parseable (<see cref="JsonWebTokenHandler.ReadJsonWebToken"/>
/// lanza), forwardea al scheme <c>Jwt</c> que va a fallar limpio con 401.
/// NO crashear ni devolver 500.
/// </para>
///
/// <para>
/// <c>ReadJsonWebToken</c> NO valida firma (solo parsea); usarlo para
/// extraer <c>iss</c> sin validar es seguro porque el handler destino
/// hace validación completa.
/// </para>
/// </summary>
public static class JwtOrEntraSpPolicyScheme
{
    public const string SchemeName = "JwtOrEntraSp";

    public static string ForwardSelector(HttpContext context, IOptions<EntraIdOptions> entraOptions)
    {
        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader)
            || authHeader.Count == 0)
        {
            return JwtBearerDefaults.AuthenticationScheme;
        }

        var raw = authHeader.ToString();
        const string prefix = "Bearer ";
        if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return JwtBearerDefaults.AuthenticationScheme;
        }

        var token = raw[prefix.Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return JwtBearerDefaults.AuthenticationScheme;
        }

        var tenantId = entraOptions.Value.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            // Sin tenant configurado, no podemos detectar tokens Entra.
            // Forwardear todo a Jwt.
            return JwtBearerDefaults.AuthenticationScheme;
        }

        try
        {
            var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
            var iss = jwt.Issuer ?? string.Empty;

            var entraIssuerV2 = $"https://login.microsoftonline.com/{tenantId}/v2.0";
            var entraIssuerV1 = $"https://sts.windows.net/{tenantId}/";

            if (string.Equals(iss, entraIssuerV2, StringComparison.OrdinalIgnoreCase)
                || string.Equals(iss, entraIssuerV1, StringComparison.OrdinalIgnoreCase))
            {
                return EntraServicePrincipalAuthenticationHandler.SchemeName;
            }
        }
        catch
        {
            // Token malformado / no JWT. Forwardear a Jwt para que falle
            // con 401 limpio (H2). NO propagar la excepción.
        }

        return JwtBearerDefaults.AuthenticationScheme;
    }
}
