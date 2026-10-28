using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Graph.Users.Item.SendMail;
using Microsoft.Extensions.Options;
using Millet.Api.Auth.Options;
using Millet.CuentasPorPagar.Domain.Notificaciones;
using Millet.CuentasPorPagar.Infrastructure.Notificaciones;
using Millet.Identidad.Application.DirectorioEntra;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Infrastructure.Stubs;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Auth.Provisioning;

/// <summary>Cliente único de Graph para el alta de colaboradores; nunca usa credenciales ficticias.</summary>
public static class GraphColaboradorRegistration
{
    public static IServiceCollection AddGraphColaboradores(
        this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        var auth = configuration.GetSection("Auth:EntraId").Get<EntraIdOptions>() ?? new();
        var entra = configuration.GetSection("Entra").Get<EntraDirectorioOptions>() ?? new();
        var correoProveedor = configuration["Entra:Correo:Proveedor"] ?? "Graph";
        var correoSandbox = correoProveedor.Equals("SandboxLocal", StringComparison.OrdinalIgnoreCase);
        if (!correoSandbox && !correoProveedor.Equals("Graph", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Entra:Correo:Proveedor debe ser Graph o SandboxLocal.");
        if (correoSandbox)
        {
            var host = entra.Simulacion.CorreoSandbox.Host;
            var esLoopback = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host == "127.0.0.1" || host == "::1";
            if (!isDevelopment || !esLoopback || entra.Simulacion.CorreoSandbox.Puerto is < 1 or > 65535)
                throw new InvalidOperationException(
                    "Graph con correo SandboxLocal solo se permite en Development y SMTP de captura en loopback.");
        }
        if (string.IsNullOrWhiteSpace(auth.TenantId) ||
            string.IsNullOrWhiteSpace(auth.ClientId) ||
            string.IsNullOrWhiteSpace(auth.ClientSecret) ||
            (!correoSandbox && string.IsNullOrWhiteSpace(auth.SenderEmail)) ||
            entra.DominiosPermitidos.Count == 0 ||
            entra.Provision.Disabled ||
            !Uri.TryCreate(entra.UrlInicioSesion, UriKind.Absolute, out var url) ||
            (!isDevelopment && url.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Graph para colaboradores requiere TenantId, ClientId, ClientSecret, SenderEmail si el correo es Graph, " +
                "Entra:DominiosPermitidos, Entra:Provision:Disabled=false y UrlInicioSesion HTTPS.");
        }

        services.AddSingleton(_ => new GraphServiceClient(
            new ClientSecretCredential(auth.TenantId, auth.ClientId, auth.ClientSecret),
            ["https://graph.microsoft.com/.default"]));
        services.AddSingleton<IEntraDirectorioPort, GraphDirectorioColaboradores>();
        if (correoSandbox)
            services.AddSingleton<ICorreoSalientePort, CorreoSandboxLocal>();
        else
            services.AddSingleton<ICorreoSalientePort, GraphCorreoColaboradores>();
        return services;
    }
}

/// <summary>Implementación real del contrato de directorio usado por el alta unificada.</summary>
public sealed class GraphDirectorioColaboradores : IEntraDirectorioPort
{
    private readonly GraphServiceClient _graph;
    private readonly IOptionsMonitor<EntraDirectorioOptions> _options;

    public GraphDirectorioColaboradores(GraphServiceClient graph, IOptionsMonitor<EntraDirectorioOptions> options)
    {
        _graph = graph;
        _options = options;
    }

    public async Task<CuentaEntra?> BuscarPorCorreoAsync(string correo, CancellationToken ct)
    {
        var upn = correo.Trim();
        // Se consulta por UPN exacto; un error de Graph no se interpreta como "no existe".
        try
        {
            var user = await _graph.Users[upn].GetAsync(r =>
                r.QueryParameters.Select = ["id", "userPrincipalName", "displayName", "accountEnabled"], ct);
            return user?.Id is null ? null : Convertir(user);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            return null;
        }
    }

    public async Task<CuentaEntraCreada> CrearCuentaAsync(SolicitudCuentaEntra solicitud, CancellationToken ct)
    {
        var upn = solicitud.Upn.Trim();
        if (!_options.CurrentValue.PermiteCorreo(upn))
            throw new BusinessRuleException("ENTRA_DOMINIO_NO_PERMITIDO", "El dominio corporativo no está permitido.");
        if (string.IsNullOrWhiteSpace(solicitud.ClaveIdempotencia))
            throw new BusinessRuleException("ENTRA_CLAVE_INVALIDA", "La clave del empleado no es válida.");

        var existente = await BuscarUsuarioCompletoAsync(upn, ct);
        if (existente is not null)
        {
            if (!string.Equals(existente.EmployeeId, solicitud.ClaveIdempotencia, StringComparison.OrdinalIgnoreCase))
                throw new ConflictException("ENTRA_UPN_EN_USO", "El UPN ya pertenece a otra cuenta Microsoft.");

            // Reintento posterior a crear la cuenta, pero previo a guardar el OID en ERP.
            var nuevaClave = await RestablecerContrasenaTemporalAsync(existente.Id!, ct);
            return new CuentaEntraCreada(Convertir(existente), nuevaClave);
        }

        var contrasena = DirectorioEntraSimulado.GenerarContrasenaTemporal();
        var user = new User
        {
            AccountEnabled = true,
            DisplayName = solicitud.NombreMostrado.Trim(),
            MailNickname = upn.Split('@')[0],
            UserPrincipalName = upn,
            EmployeeId = solicitud.ClaveIdempotencia,
            UsageLocation = "MX",
            PasswordProfile = new PasswordProfile
            {
                Password = contrasena,
                ForceChangePasswordNextSignIn = true
            }
        };
        try
        {
            var creada = await _graph.Users.PostAsync(user, cancellationToken: ct);
            if (creada?.Id is null)
                throw new InvalidOperationException("Graph no devolvió el OID de la cuenta creada.");
            return new CuentaEntraCreada(new CuentaEntra(
                creada.Id, creada.UserPrincipalName ?? upn,
                creada.DisplayName ?? solicitud.NombreMostrado.Trim(), creada.AccountEnabled != false), contrasena);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 409)
        {
            // Si otra instancia creó el UPN al mismo tiempo, la próxima ejecución
            // verificará employeeId antes de adoptar la cuenta. Nunca se adopta a ciegas.
            throw new ConflictException("ENTRA_UPN_EN_USO", "Graph rechazó el UPN; verifique si ya existe.");
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403 || string.Equals(ex.Error?.Code, "Authorization_RequestDenied", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(
                "ENTRA_PERMISOS_INSUFICIENTES",
                "Microsoft Entra ID rechazó la creación de cuenta por permisos insuficientes en el tenant.");
        }
        catch (ODataError ex)
        {
            throw new BusinessRuleException(
                "ENTRA_ERROR_CREACION_CUENTA",
                $"Microsoft Entra ID rechazó la creación de cuenta: {ex.Error?.Message ?? ex.Message}");
        }
    }

    public async Task<string> RestablecerContrasenaTemporalAsync(string objectId, CancellationToken ct)
    {
        var contrasena = DirectorioEntraSimulado.GenerarContrasenaTemporal();
        try
        {
            await _graph.Users[objectId].PatchAsync(new User
            {
                PasswordProfile = new PasswordProfile
                {
                    Password = contrasena,
                    ForceChangePasswordNextSignIn = true
                }
            }, cancellationToken: ct);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            throw new EntityNotFoundException("ENTRA_CUENTA_NO_ENCONTRADA", "La cuenta Microsoft ya no existe.");
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403 || string.Equals(ex.Error?.Code, "Authorization_RequestDenied", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(
                "ENTRA_PERMISOS_INSUFICIENTES",
                "Microsoft Entra ID rechazó el restablecimiento de contraseña (403 Authorization_RequestDenied). La aplicación registrada en Azure requiere el rol de directorio 'User Administrator' (Administrador de usuarios) o 'Helpdesk Administrator'.");
        }
        catch (ODataError ex)
        {
            throw new BusinessRuleException(
                "ENTRA_ERROR_RESTABLECER_CONTRASENA",
                $"Microsoft Entra ID rechazó el restablecimiento de contraseña: {ex.Error?.Message ?? ex.Message}");
        }
        return contrasena;
    }

    private async Task<User?> BuscarUsuarioCompletoAsync(string upn, CancellationToken ct)
    {
        try
        {
            return await _graph.Users[upn].GetAsync(r =>
                r.QueryParameters.Select = ["id", "userPrincipalName", "displayName", "accountEnabled", "employeeId"], ct);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            return null;
        }
    }

    private static CuentaEntra Convertir(User user) => new(
        user.Id!, user.UserPrincipalName ?? string.Empty,
        user.DisplayName ?? string.Empty, user.AccountEnabled == true);
}

/// <summary>Solicita el envío desde el buzón configurado; un error nunca se oculta.</summary>
public sealed class GraphCorreoColaboradores : ICorreoSalientePort
{
    private readonly GraphServiceClient _graph;
    private readonly string _sender;

    public GraphCorreoColaboradores(GraphServiceClient graph, IOptions<EntraIdOptions> options)
    {
        _graph = graph;
        _sender = options.Value.SenderEmail;
    }

    public async Task EnviarAccesoColaboradorAsync(CorreoAccesoColaborador correo, CancellationToken ct)
    {
        var nombre = System.Net.WebUtility.HtmlEncode(correo.NombreColaborador);
        var upn = System.Net.WebUtility.HtmlEncode(correo.Upn);
        var contrasena = System.Net.WebUtility.HtmlEncode(correo.ContrasenaTemporal);
        var url = System.Net.WebUtility.HtmlEncode(correo.UrlInicioSesion);
        var body = $"<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto'>" +
                   $"<h2>Bienvenido al ERP Millet</h2><p>Hola {nombre}, tu usuario es <strong>{upn}</strong>.</p>" +
                   $"<p>Contraseña temporal: <strong>{contrasena}</strong></p>" +
                   "<p>Microsoft te solicitará cambiarla en el primer inicio de sesión.</p>" +
                   $"<p><a href='{url}' style='display:inline-block;padding:12px 20px;background:#4F46E5;color:#fff;text-decoration:none;border-radius:6px'>Iniciar sesión en el ERP</a></p>" +
                   $"<p>Si el botón no abre, copia este enlace: {url}</p>" +
                   "<p>Si no solicitaste este acceso, contacta al administrador.</p></div>";
        await EnviarHtmlAsync(_graph, _sender, [correo.Destinatario], "Acceso al ERP Millet", body, ct);
    }

    /// <summary>Envío HTML por Graph desde <paramref name="sender"/>; compartido con los avisos de CxP.</summary>
    internal static async Task EnviarHtmlAsync(
        GraphServiceClient graph, string sender, IEnumerable<string> para, string asunto, string html, CancellationToken ct)
    {
        try
        {
            await graph.Users[sender].SendMail.PostAsync(new SendMailPostRequestBody
            {
                Message = new Message
                {
                    Subject = asunto,
                    Body = new ItemBody { ContentType = BodyType.Html, Content = html },
                    ToRecipients = [.. para.Select(a => new Recipient { EmailAddress = new EmailAddress { Address = a } })]
                },
                SaveToSentItems = true
            }, cancellationToken: ct);
            // Graph confirma aceptación de la solicitud, no entrega al buzón.
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403 || string.Equals(ex.Error?.Code, "Authorization_RequestDenied", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(
                "CORREO_PERMISOS_INSUFICIENTES",
                "Microsoft Entra ID rechazó el envío de correo por permisos insuficientes. La aplicación requiere el permiso 'Mail.Send' en el tenant.");
        }
        catch (ODataError ex)
        {
            throw new BusinessRuleException(
                "CORREO_ERROR_ENVIO",
                $"Error al enviar correo vía Microsoft Graph: {ex.Error?.Message ?? ex.Message}");
        }
    }
}

/// <summary>
/// Registro de los avisos SLA de CxP por Graph. Independiente del alta de colaboradores: solo exige
/// Auth:EntraId (TenantId, ClientId, ClientSecret, SenderEmail) y al menos un Contador General válido.
/// Usa su propio cliente de Graph (no el singleton de colaboradores).
/// </summary>
public static class GraphAvisosRevisionRegistration
{
    public static IServiceCollection AddGraphAvisosRevisionCxp(this IServiceCollection services, IConfiguration configuration)
    {
        var avisos = configuration.GetSection(AvisosRevisionOptions.SectionName).Get<AvisosRevisionOptions>() ?? new();
        if (!avisos.Proveedor.Equals("Graph", StringComparison.OrdinalIgnoreCase)) return services;
        var auth = configuration.GetSection("Auth:EntraId").Get<EntraIdOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(auth.TenantId) || string.IsNullOrWhiteSpace(auth.ClientId) ||
            string.IsNullOrWhiteSpace(auth.ClientSecret) || string.IsNullOrWhiteSpace(auth.SenderEmail))
            throw new InvalidOperationException(
                $"{AvisosRevisionOptions.SectionName}:Proveedor=Graph requiere Auth:EntraId:TenantId, ClientId, ClientSecret y SenderEmail.");
        if (avisos.ContadorGeneral.All(c => !AvisosRevisionOptions.EsCorreoValido(c)))
            throw new InvalidOperationException(
                $"{AvisosRevisionOptions.SectionName}:Proveedor=Graph requiere al menos un correo válido en {AvisosRevisionOptions.SectionName}:ContadorGeneral.");
        services.AddSingleton<INotificacionService>(sp => new GraphAvisoRevisionCxp(
            new GraphServiceClient(
                new ClientSecretCredential(auth.TenantId, auth.ClientId, auth.ClientSecret),
                ["https://graph.microsoft.com/.default"]),
            auth.SenderEmail, avisos, sp.GetRequiredService<ILogger<GraphAvisoRevisionCxp>>()));
        return services;
    }
}

/// <summary>Correo del aviso SLA de factura en revisión al Contador General (y Compras según el motivo).</summary>
public sealed class GraphAvisoRevisionCxp : INotificacionService
{
    private readonly GraphServiceClient _graph;
    private readonly string _sender;
    private readonly AvisosRevisionOptions _avisos;
    private readonly ILogger<GraphAvisoRevisionCxp> _logger;

    public GraphAvisoRevisionCxp(
        GraphServiceClient graph, string sender, AvisosRevisionOptions avisos, ILogger<GraphAvisoRevisionCxp> logger)
    {
        _graph = graph;
        _sender = sender;
        _avisos = avisos;
        _logger = logger;
    }

    public async Task NotificarSlaRevisionAsync(SlaRevisionNotificacion notificacion, CancellationToken cancellationToken)
    {
        var para = _avisos.Destinatarios(notificacion);
        if (para.Count == 0)
        {
            _logger.LogWarning("[GraphAvisoRevisionCxp] Sin destinatarios en {Seccion}; el aviso de factura {Factura} no se envió.",
                AvisosRevisionOptions.SectionName, notificacion.FacturaProveedorId);
            return;
        }
        await GraphCorreoColaboradores.EnviarHtmlAsync(
            _graph, _sender, para, $"Factura en revisión: {NombreNivel(notificacion.Nivel)}", ConstruirHtml(notificacion), cancellationToken);
    }

    public static string NombreNivel(NivelEscalamientoSla nivel) => nivel switch
    {
        NivelEscalamientoSla.AvisoGerente => "primer aviso",
        NivelEscalamientoSla.SlaVencido => "plazo vencido",
        _ => "escalamiento",
    };

    public static string ConstruirHtml(SlaRevisionNotificacion n)
    {
        static string E(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
        return "<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto'>" +
               "<h2>Factura de proveedor en revisión</h2>" +
               $"<p>Factura <strong>{E(n.Folio ?? "sin folio")}</strong> con motivo «{E(n.MotivoNombre)}».</p>" +
               $"<p>Lleva <strong>{n.DiasEnRevision}</strong> días hábiles en revisión (plazo: {n.SlaDias}). Nivel: {E(NombreNivel(n.Nivel))}.</p>" +
               "<p>Revísala en el ERP, bandeja «Revisión por área».</p></div>";
    }
}
