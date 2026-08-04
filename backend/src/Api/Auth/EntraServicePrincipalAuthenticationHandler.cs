using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Identidad.Application.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Api.Auth;

/// <summary>
/// Authentication scheme <c>EntraServicePrincipal</c> (PR A): valida
/// tokens de Entra emitidos via client_credentials y arma un
/// <see cref="ClaimsPrincipal"/> con los claims que el resto del stack
/// (idempotencia, audit_log, query filters, RBAC) necesita.
///
/// <para>
/// <b>Comportamiento (D-HANDLER del prompt PR A):</b>
/// <list type="bullet">
///   <item>Token NO es de SP (humano delegado) → <see cref="AuthenticateResult.NoResult"/>:
///         permite que el <see cref="JwtOrEntraSpPolicyScheme"/> forwardee
///         al scheme <c>Jwt</c> clásico.</item>
///   <item>Token de SP con firma inválida / expirado / aud incorrecto →
///         <c>AuthenticateResult.Fail</c> con type
///         <c>auth/expired_token</c>, <c>auth/invalid_signature</c>, etc.
///         Genera 401.</item>
///   <item>Token de SP válido pero AppId NO registrado en BD →
///         <c>Fail</c> + override de challenge a 403
///         <c>auth/unknown_service_principal</c>.</item>
///   <item>Token de SP válido pero <c>UsuarioServicio.Activo=false</c> →
///         <c>Fail</c> + override de challenge a 403
///         <c>auth/service_principal_disabled</c>.</item>
///   <item>Token de SP válido + SP resuelto + Activo → <c>Success</c> con
///         claims <c>sub</c>=<c>UsuarioServicio.Id</c>,
///         <c>current_empresa_id</c>=<c>EmpresaId</c>,
///         <c>auth_type=service_principal</c>, <c>entra_appid</c> y
///         <c>permission</c>×N.</item>
/// </list>
/// </para>
///
/// <para>
/// La elección de <c>sub</c>=<c>UsuarioServicio.Id</c> es deliberada: el
/// resto del stack lee <see cref="ICurrentUserContext.UserId"/> (que mapea
/// al claim <c>sub</c>), y eso debe devolver el id del SP. Así
/// <c>IdempotencyMiddleware</c>, <c>AuditSaveChangesInterceptor</c> y los
/// query filters tratan al SP como un "usuario" sin saberlo. <see cref="ICurrentServicePrincipal"/>
/// expone los datos específicos de SP cuando se necesitan explícitamente.
/// </para>
/// </summary>
public sealed class EntraServicePrincipalAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "EntraServicePrincipal";

    private const string FailureKey = "EntraSp.Failure";

    private readonly IEntraTokenValidator _tokenValidator;
    private readonly IServicePrincipalResolver _spResolver;

    public EntraServicePrincipalAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IEntraTokenValidator tokenValidator,
        IServicePrincipalResolver spResolver) : base(options, logger, encoder)
    {
        _tokenValidator = tokenValidator;
        _spResolver = spResolver;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader)
            || authHeader.Count == 0)
        {
            return AuthenticateResult.NoResult();
        }

        var raw = authHeader.ToString();
        const string bearerPrefix = "Bearer ";
        if (!raw.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = raw[bearerPrefix.Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        ValidatedServicePrincipalToken? validated;
        try
        {
            validated = await _tokenValidator.TryValidateServicePrincipalAsync(token, Context.RequestAborted);
        }
        catch (UnauthorizedAccessException ex)
        {
            // Firma inválida / expirado / iss / aud / etc. NO podemos
            // forwardear porque ya sabemos que es un token Entra (el
            // PolicyScheme lo enrutó a este handler). Fail genera 401.
            Logger.LogWarning(ex, "Token de service principal rechazado por validación.");
            return AuthenticateResult.Fail(ex);
        }

        if (validated is null)
        {
            // Token Entra válido pero NO es de service principal (es de
            // humano delegado). Devolvemos NoResult para que el PolicyScheme
            // forwardee al scheme Jwt clásico (donde fallará limpio porque
            // un token Entra no es un API JWT, pero al menos seguimos el
            // flujo correcto).
            return AuthenticateResult.NoResult();
        }

        var resolution = await _spResolver.ResolveAsync(
            validated.AppId, validated.ObjectId, Context.RequestAborted);

        if (resolution.Failure == ServicePrincipalResolutionFailure.UnknownAppId)
        {
            Logger.LogWarning(
                "Service principal con AppId={AppId} no registrado en BD. Devuelve 403 unknown_service_principal.",
                validated.AppId);
            Context.Items[FailureKey] = SpFailureKind.Unknown;
            return AuthenticateResult.Fail("Unknown service principal.");
        }

        if (resolution.Failure == ServicePrincipalResolutionFailure.Disabled)
        {
            Logger.LogWarning(
                "Service principal AppId={AppId} existe pero está desactivado. Devuelve 403 service_principal_disabled.",
                validated.AppId);
            Context.Items[FailureKey] = SpFailureKind.Disabled;
            return AuthenticateResult.Fail("Service principal disabled.");
        }

        var sp = resolution.Principal!;

        var identity = new ClaimsIdentity(
            authenticationType: SchemeName,
            nameType: MilletClaimTypes.Name,
            roleType: ClaimTypes.Role);

        identity.AddClaim(new Claim(MilletClaimTypes.Sub, sp.Id.ToString()));
        identity.AddClaim(new Claim(MilletClaimTypes.Name, sp.Nombre));
        identity.AddClaim(new Claim(MilletClaimTypes.CurrentEmpresaId, sp.EmpresaId.ToString()));
        identity.AddClaim(new Claim(MilletClaimTypes.AuthType, MilletClaimTypes.AuthTypes.ServicePrincipal));
        identity.AddClaim(new Claim(MilletClaimTypes.EntraAppId, sp.EntraAppId.ToString()));

        foreach (var permiso in sp.Permisos)
        {
            identity.AddClaim(new Claim(MilletClaimTypes.Permission, permiso));
        }

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    /// <summary>
    /// Override del challenge para emitir 403 con Problem Details en los
    /// casos SP-specific (unknown / disabled) que api-contract §3.7 marca
    /// como 403 (no 401). Para fallas de validación de token Entra (firma,
    /// expiry), cae al comportamiento default (401 sin body).
    /// </summary>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items.TryGetValue(FailureKey, out var failureObj)
            && failureObj is SpFailureKind kind)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            Response.ContentType = "application/problem+json";

            var (typeUri, title, detail) = kind switch
            {
                SpFailureKind.Unknown => (
                    "auth/unknown_service_principal",
                    "Service principal no registrado",
                    "El AppId del token no corresponde a ningún UsuarioServicio registrado en el sistema."),
                SpFailureKind.Disabled => (
                    "auth/service_principal_disabled",
                    "Service principal desactivado",
                    "El UsuarioServicio asociado al AppId está marcado Activo=false."),
                _ => ("auth/unknown_error", "Error de auth", "Razón desconocida."),
            };

            var problem = new
            {
                type = $"https://millet-erp/problems/{typeUri}",
                title,
                status = StatusCodes.Status403Forbidden,
                detail,
            };

            await JsonSerializer.SerializeAsync(Response.Body, problem, cancellationToken: Context.RequestAborted);
            return;
        }

        // Fallback: comportamiento default (401 challenge sin body).
        await base.HandleChallengeAsync(properties);
    }

    private enum SpFailureKind
    {
        Unknown = 1,
        Disabled = 2,
    }
}
