using Azure.Identity;
using Microsoft.Graph;
using Millet.Api.Auth.Options;
using Millet.CuentasPorPagar.Domain.Ports.Portal;

namespace Millet.Api.Auth.Provisioning;

/// <summary>
/// Código del portal de proveedores por Graph desde el buzón de Auth:EntraId:SenderEmail (erpmillet@).
/// Se activa con <c>CuentasPorPagar:PortalProveedores:Proveedor=Graph</c>; sin eso queda el NoOp de CxP.
/// </summary>
public static class GraphPortalProveedorRegistration
{
    public const string Seccion = "CuentasPorPagar:PortalProveedores";

    public static IServiceCollection AddGraphPortalProveedorCorreo(this IServiceCollection services, IConfiguration configuration)
    {
        if (!string.Equals(configuration[$"{Seccion}:Proveedor"], "Graph", StringComparison.OrdinalIgnoreCase)) return services;
        var auth = configuration.GetSection("Auth:EntraId").Get<EntraIdOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(auth.TenantId) || string.IsNullOrWhiteSpace(auth.ClientId) ||
            string.IsNullOrWhiteSpace(auth.ClientSecret) || string.IsNullOrWhiteSpace(auth.SenderEmail))
            throw new InvalidOperationException(
                $"{Seccion}:Proveedor=Graph requiere Auth:EntraId:TenantId, ClientId, ClientSecret y SenderEmail.");
        services.AddSingleton<IPortalCorreoPort>(_ => new GraphPortalProveedorCorreo(
            new GraphServiceClient(new ClientSecretCredential(auth.TenantId, auth.ClientId, auth.ClientSecret),
                ["https://graph.microsoft.com/.default"]),
            auth.SenderEmail));
        return services;
    }
}

public sealed class GraphPortalProveedorCorreo(GraphServiceClient graph, string sender) : IPortalCorreoPort
{
    public Task EnviarCodigoAsync(string destinatario, string codigo, CancellationToken cancellationToken) =>
        GraphCorreoColaboradores.EnviarHtmlAsync(graph, sender, [destinatario], "Tu código de acceso al portal de proveedores de Millet",
            ConstruirHtml(codigo), cancellationToken);

    public Task EnviarAvisoPagoAsync(string destinatario, AvisoPagoCorreo aviso, CancellationToken cancellationToken) =>
        GraphCorreoColaboradores.EnviarHtmlAsync(graph, sender, [destinatario], "Millet te realizó un pago",
            ConstruirHtmlPago(aviso), cancellationToken);

    public static string ConstruirHtmlPago(AvisoPagoCorreo a)
    {
        static string E(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
        return "<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto'>" +
               "<h2>Pago aplicado a tu factura</h2>" +
               $"<p>Registramos un pago de <strong>{a.Monto:N2} {E(a.Moneda)}</strong> el {a.FechaPago:dd/MM/yyyy} a la factura <strong>{E(a.FolioFactura ?? "sin folio")}</strong>.</p>" +
               "<p>Si la factura es de pago en parcialidades o diferido, recuerda subir tu complemento de pago en el portal de proveedores.</p></div>";
    }

    public static string ConstruirHtml(string codigo) =>
        "<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto'>" +
        "<h2>Portal de proveedores Millet</h2>" +
        $"<p>Tu código de acceso es <strong style='font-size:24px;letter-spacing:4px'>{System.Net.WebUtility.HtmlEncode(codigo)}</strong></p>" +
        "<p>Vence en 10 minutos. Si no lo solicitaste, ignora este correo.</p></div>";
}
