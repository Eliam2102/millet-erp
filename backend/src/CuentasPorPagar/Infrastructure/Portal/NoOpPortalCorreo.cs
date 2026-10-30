using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Ports.Portal;

namespace Millet.CuentasPorPagar.Infrastructure.Portal;

/// <summary>
/// Sin transporte de correo (Graph se activa con <c>CuentasPorPagar:PortalProveedores:Proveedor=Graph</c>).
/// Solo en Development escribe el código en el log para poder probar a mano; en cualquier otro ambiente no lo registra.
/// </summary>
public sealed class NoOpPortalCorreo(ILogger<NoOpPortalCorreo> logger, IHostEnvironment env) : IPortalCorreoPort
{
    public Task EnviarCodigoAsync(string destinatario, string codigo, CancellationToken cancellationToken)
    {
        if (env.IsDevelopment())
            logger.LogWarning("[PortalProveedor][DEV] Código de acceso para {Destinatario}: {Codigo}", destinatario, codigo);
        else
            logger.LogWarning("[PortalProveedor] Sin transporte de correo configurado; no se envió el código.");
        return Task.CompletedTask;
    }

    public Task EnviarAvisoPagoAsync(string destinatario, AvisoPagoCorreo aviso, CancellationToken cancellationToken)
    {
        logger.LogInformation("[PortalProveedor] Sin transporte de correo; aviso de pago de la factura {Folio} no enviado.", aviso.FolioFactura);
        return Task.CompletedTask;
    }
}
