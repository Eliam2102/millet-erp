using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Millet.Api.Auth.Options;
using Millet.Compartido.Application.Ports;
using Millet.Compartido.Infrastructure.Adapters;
using Millet.Identidad.Application;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Infrastructure;
using Millet.Identidad.Infrastructure.Adapters;
using Millet.Identidad.Infrastructure.Telemetry;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Api.Auth;

/// <summary>
/// Extension methods para wirear todos los servicios y middleware de auth
/// del API. Idempotente y composable: se llama una vez desde Program.cs.
/// </summary>
public static class AuthExtensions
{
    /// <summary>
    /// Registra:
    /// <list type="bullet">
    ///   <item>Options bindeadas (<c>Auth:Jwt</c>, <c>Auth:EntraId</c>) con validación al arranque</item>
    ///   <item>JwtBearer authentication scheme — valida el JWT del API (HMAC) en cada request</item>
    ///   <item>AuthorizationServices (default policy)</item>
    ///   <item><c>IHttpContextAccessor</c> — necesario para que <c>CurrentUserContext</c> y <c>CurrentEmpresaContext</c> lean claims</item>
    ///   <item><c>IJwtTokenService</c>, <c>IEntraTokenValidator</c>, <c>LoginOrchestrator</c></item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddMilletAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtIssuanceOptions>()
            .Bind(configuration.GetSection("Auth:Jwt"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey),
                "Auth:Jwt:SigningKey es requerido (en Development viene de appsettings.Development.json; en QA/Prod de Key Vault).")
            .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey) >= 32,
                "Auth:Jwt:SigningKey debe tener al menos 32 bytes (256 bits) para HMAC SHA-256.")
            .ValidateOnStart();

        services.AddOptions<EntraIdOptions>()
            .Bind(configuration.GetSection("Auth:EntraId"));

        var jwtOptions = configuration.GetSection("Auth:Jwt").Get<JwtIssuanceOptions>()
            ?? throw new InvalidOperationException("Auth:Jwt section is missing in configuration.");

        var signingKeyBytes = Encoding.UTF8.GetBytes(jwtOptions.SigningKey);
        var signingKey = new SymmetricSecurityKey(signingKeyBytes);

        // PR A: default scheme = "JwtOrEntraSp" (PolicyScheme) que enruta
        // por `iss` del token al scheme correcto. Para tokens humanos
        // (API JWT, iss=millet-erp-api) → "Jwt". Para tokens Entra
        // (iss=login.microsoftonline.com/{tenant}/v2.0 o sts.windows.net)
        // → "EntraServicePrincipal". Tokens malformados o sin Bearer →
        // forward a "Jwt" que falla limpio con 401.
        services.AddAuthentication(JwtOrEntraSpPolicyScheme.SchemeName)
            .AddPolicyScheme(JwtOrEntraSpPolicyScheme.SchemeName, displayName: null, options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var entraOpts = context.RequestServices.GetRequiredService<IOptions<EntraIdOptions>>();
                    return JwtOrEntraSpPolicyScheme.ForwardSelector(context, entraOpts);
                };
            })
            .AddScheme<AuthenticationSchemeOptions, EntraServicePrincipalAuthenticationHandler>(
                EntraServicePrincipalAuthenticationHandler.SchemeName,
                displayName: "Entra Service Principal",
                configureOptions: null)
            .AddJwtBearer(options =>
            {
                // Preservar nombres originales del JWT (sub, email, name,
                // current_empresa_id) en lugar de remapearlos a ClaimTypes.*.
                // Match con MilletClaimTypes y simplifica los readers.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = MilletClaimTypes.Name, // User.Identity.Name
                };

                // CollaborationHub Sprint 1 (ADR-0001 + ADR-0012 Capa 2):
                // SignalR usa WebSockets que no permiten headers custom desde
                // el navegador, así que el cliente SignalR pasa el JWT como
                // query param `access_token` para conexiones a /hubs/*.
                // Para REST normal (Authorization header) el evento es no-op.
                options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken)
                            && path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();
        services.AddHttpContextAccessor();

        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IEntraTokenValidator, EntraTokenValidator>();
        services.AddScoped<LoginOrchestrator>();

        // RBAC granular (ADR-0007). El policy provider construye policies
        // dinámicamente para cualquier nombre con prefijo "permiso:"; el
        // handler resuelve cada PermissionRequirement contra
        // IPermissionCache (con TTL 5 min) y, en miss, IPermissionLoader
        // que consulta la BD.
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IPermissionLoader, PermissionLoader>();

        // El 403 de policy sale del middleware de autorización con body
        // vacío (no pasa por GlobalExceptionHandler); este handler lo
        // convierte en Problem Details nombrando el permiso faltante.
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, PermisoFaltanteResultHandler>();

        // Permisos efectivos consultables desde handlers (scoping fino de
        // datos, 12-cajas.md §9). Mismo dual-path humano/SP y mismo cache
        // que PermissionAuthorizationHandler.
        services.AddScoped<ICurrentUserPermissions, CurrentUserPermissions>();

        // Bootstrap del primer SuperAdmin (ADR-0007). El hosted service corre
        // en arranque y es idempotente — re-arranques verifican el estado y
        // no duplican nada. Si Auth:InitialAdminEntraOid está vacío, omite
        // el bootstrap (útil para tests y entornos sin admin configurado).
        services.AddOptions<BootstrapSuperAdminOptions>()
            .Bind(configuration.GetSection(BootstrapSuperAdminOptions.SectionName));
        services.AddHostedService<BootstrapSuperAdminHostedService>();

        // PR A: service principal pipeline.
        // - IdentidadMeter: singleton para counters custom (auth.sp.*).
        //   Registrado en WithMetrics(...) del bootstrap OpenTelemetry
        //   en Program.cs (ver llamada AddMeter(IdentidadMeter.Name)).
        // - DefaultServicePrincipalResolver lee BD; el cache decorator
        //   in-memory por TTL 5 min lo envuelve. El handler de auth
        //   inyecta el cached resolver (lookup directo de la interfaz).
        // - IEmpresaResolverPort: impl vive en Compartido (cross-context
        //   sin acoplar a CompartidoDbContext desde Identidad bootstrap).
        // - ICurrentServicePrincipal: scoped, lee del HttpContext.User.
        // - BootstrapServicePrincipalsHostedService: sincroniza el catálogo
        //   de SPs desde Auth:ServicePrincipalsJson en arranque.
        services.AddMemoryCache();
        services.AddSingleton<IdentidadMeter>();
        services.AddScoped<DefaultServicePrincipalResolver>();
        services.AddScoped<IServicePrincipalResolver>(sp =>
        {
            var inner = sp.GetRequiredService<DefaultServicePrincipalResolver>();
            var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            return new CachedServicePrincipalResolver(inner, cache);
        });
        services.AddScoped<IEmpresaResolverPort, EmpresaResolverAdapter>();
        services.AddScoped<ICurrentServicePrincipal, CurrentServicePrincipal>();

        services.AddOptions<BootstrapServicePrincipalsOptions>()
            .Configure<IConfiguration>((opts, cfg) =>
            {
                // Auth:ServicePrincipalsJson es un string (JSON serializado),
                // no un objeto. Lo leemos directo desde IConfiguration sin
                // bind automático (que esperaría un POCO nested).
                opts.Json = cfg["Auth:ServicePrincipalsJson"] ?? string.Empty;
            });
        services.AddHostedService<BootstrapServicePrincipalsHostedService>();

        return services;
    }
}
