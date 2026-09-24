using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Graph.Users.Item.SendMail;
using Microsoft.Extensions.Options;
using Millet.Api.Auth.Options;
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
            url.Scheme != Uri.UriSchemeHttps)
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
        if (string.IsNullOrWhiteSpace(_sender))
            throw new BusinessRuleException("CORREO_ACCESO_NO_CONFIGURADO", "Falta el buzón remitente.");
        var body = $"Hola {correo.NombreColaborador},\n\n" +
                   $"Tu usuario para el ERP Millet es: {correo.Upn}\n" +
                   $"Contraseña temporal: {correo.ContrasenaTemporal}\n\n" +
                   "Microsoft te solicitará cambiarla en el primer inicio de sesión.\n" +
                   $"Accede aquí: {correo.UrlInicioSesion}\n\n" +
                   "Si no solicitaste este acceso, contacta al administrador.";
        await _graph.Users[_sender].SendMail.PostAsync(new SendMailPostRequestBody
        {
            Message = new Message
            {
                Subject = "Acceso al ERP Millet",
                Body = new ItemBody { ContentType = BodyType.Text, Content = body },
                ToRecipients = [new Recipient { EmailAddress = new EmailAddress { Address = correo.Destinatario } }]
            },
            SaveToSentItems = true
        }, cancellationToken: ct);
        // Graph confirma aceptación de la solicitud, no entrega al buzón.
    }
}
