using Azure.Identity;
using Microsoft.Graph;
using Millet.Api.Auth.Options;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Correo;
using Millet.SharedKernel.Infrastructure.Correo;

namespace Millet.Api.Correo;

/// <summary>
/// Registro del transporte único de correo saliente (86e3txv0n, ADR-0063):
/// <c>Correo:Proveedor</c> = NoOp | SandboxLocal | Graph, envuelto en el modo
/// pruebas y en el registro de envíos. Con una configuración incompleta la API
/// no arranca y el mensaje nombra lo que falta.
/// </summary>
public static class CorreoSalienteRegistration
{
    public static IServiceCollection AddCorreoSaliente(
        this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        var seccion = configuration.GetSection(CorreoSalienteOptions.SectionName);
        var opciones = seccion.Get<CorreoSalienteOptions>() ?? new();
        var auth = configuration.GetSection("Auth:EntraId").Get<EntraIdOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(opciones.Remitente)) opciones.Remitente = auth.SenderEmail;
        Validar(opciones, auth, isDevelopment);

        services.AddOptions<CorreoSalienteOptions>().Bind(seccion)
            .PostConfigure(o => { if (string.IsNullOrWhiteSpace(o.Remitente)) o.Remitente = auth.SenderEmail; });

        var proveedor = opciones.EsGraph ? "Graph" : opciones.EsSandboxLocal ? "SandboxLocal" : "NoOp";
        services.AddSingleton<ICorreoSaliente>(sp =>
        {
            ICorreoSaliente transporte = proveedor switch
            {
                "Graph" => new LimitesCorreoSaliente(new GraphCorreoSaliente(
                    new GraphSendMail(new GraphServiceClient(
                        new ClientSecretCredential(auth.TenantId, auth.ClientId, auth.ClientSecret),
                        ["https://graph.microsoft.com/.default"])),
                    opciones.Remitente.Trim(), opciones.NombreRemitente), opciones.Limites, TimeProvider.System),
                "SandboxLocal" => ActivatorUtilities.CreateInstance<SmtpSandboxCorreoSaliente>(sp),
                _ => ActivatorUtilities.CreateInstance<NoOpCorreoSaliente>(sp),
            };
            var registrado = new RegistroCorreoSaliente(transporte, proveedor,
                sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<IClock>(),
                sp.GetRequiredService<ILogger<RegistroCorreoSaliente>>());
            return new ModoPruebasCorreoSaliente(registrado, opciones.ModoPruebas);
        });

        if (opciones.EsGraph && opciones.SecretoVenceEl is { } vence)
        {
            var aviso = new VencimientoSecretoCorreo(vence, TimeSpan.FromDays(opciones.AvisoSecretoDiasAntes));
            services.AddSingleton(aviso);
            services.AddHostedService<VencimientoSecretoCorreoWorker>();
            services.AddHealthChecks().AddCheck<VencimientoSecretoCorreoHealthCheck>("correo-secreto-graph", tags: ["correo"]);
        }
        return services;
    }

    /// <summary>Internal para tests: las mismas reglas con las que arranca la API.</summary>
    public static void Validar(CorreoSalienteOptions opciones, EntraIdOptions auth, bool isDevelopment)
    {
        const string s = CorreoSalienteOptions.SectionName;
        if (!opciones.EsGraph && !opciones.EsSandboxLocal && !opciones.EsNoOp)
            throw new InvalidOperationException($"{s}:Proveedor debe ser NoOp, SandboxLocal o Graph (valor: «{opciones.Proveedor}»).");

        if (opciones.EsSandboxLocal &&
            (!isDevelopment || !opciones.Smtp.EsLoopback || opciones.Smtp.Puerto is < 1 or > 65535))
            throw new InvalidOperationException(
                $"{s}:Proveedor=SandboxLocal solo se permite en Development con SMTP de captura en loopback ({s}:Smtp:Host y Puerto).");

        if (opciones.EsGraph)
        {
            var faltan = new List<string>();
            if (string.IsNullOrWhiteSpace(auth.TenantId)) faltan.Add("Auth:EntraId:TenantId");
            if (string.IsNullOrWhiteSpace(auth.ClientId)) faltan.Add("Auth:EntraId:ClientId");
            if (string.IsNullOrWhiteSpace(auth.ClientSecret)) faltan.Add("Auth:EntraId:ClientSecret");
            if (string.IsNullOrWhiteSpace(opciones.Remitente) || !opciones.Remitente.Contains('@'))
                faltan.Add($"{s}:Remitente (o Auth:EntraId:SenderEmail)");
            if (faltan.Count > 0)
                throw new InvalidOperationException(
                    $"{s}:Proveedor=Graph no puede arrancar: falta configurar {string.Join(", ", faltan)}.");
        }

        // Topes de Exchange Online para erpmillet@ (Jorge Toache, 10-nov-2026): 30 mensajes/min,
        // 500 destinatarios/mensaje, 10,000 destinatarios/24 h. La configuración no puede pasarlos.
        var l = opciones.Limites;
        if (l.MensajesPorMinuto is < 1 or > 30 || l.MaxDestinatariosPorMensaje is < 1 or > 500
            || l.MaxDestinatariosPorDia is < 1 or > 10_000 || l.Reintentos < 0 || l.EsperaInicialSegundos < 0)
            throw new InvalidOperationException(
                $"{s}:Limites fuera de rango: MensajesPorMinuto 1-30, MaxDestinatariosPorMensaje 1-500, " +
                "MaxDestinatariosPorDia 1-10000, Reintentos y EsperaInicialSegundos >= 0.");

        var pruebas = opciones.ModoPruebas;
        if (pruebas.Activo && !pruebas.BuzonesPrueba.Any(b => b.Contains('@')))
            throw new InvalidOperationException(
                $"{s}:ModoPruebas:Activo=true requiere al menos un buzón en {s}:ModoPruebas:BuzonesPrueba.");
    }
}
