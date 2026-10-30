using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Ports.Portal;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Application.Portal;

/// <summary>
/// Envía los avisos de pago pendientes a los correos autorizados del proveedor. Lo corre el worker
/// (con bypass de empresa). Un aviso se marca enviado aunque el proveedor no tenga correos autorizados;
/// si el envío falla se deja pendiente para el siguiente ciclo.
/// </summary>
public sealed record EnviarAvisosPagoPortalCommand : IRequest<int>;

public sealed class EnviarAvisosPagoPortalHandler(
    CuentasPorPagarDbContext db, IPortalCorreoPort correo, IClock clock, ILogger<EnviarAvisosPagoPortalHandler> logger)
    : IRequestHandler<EnviarAvisosPagoPortalCommand, int>
{
    public async Task<int> Handle(EnviarAvisosPagoPortalCommand request, CancellationToken cancellationToken)
    {
        var pendientes = await db.AvisosPagoPortal.Where(a => a.EnviadoEn == null).OrderBy(a => a.CreatedAt).Take(50).ToListAsync(cancellationToken);
        var enviados = 0;
        foreach (var aviso in pendientes)
        {
            var factura = await db.FacturasProveedor.AsNoTracking().FirstOrDefaultAsync(f => f.Id == aviso.FacturaProveedorId, cancellationToken);
            var correos = factura is null ? [] : await db.AccesosPortalProveedor.AsNoTracking()
                .Where(a => a.ProveedorId == factura.ProveedorId && a.Activo).Select(a => a.Email).ToListAsync(cancellationToken);
            try
            {
                var contenido = new AvisoPagoCorreo(
                    factura is null ? null : PortalMapeo.Folio(factura.SerieProveedor, factura.FolioProveedor),
                    aviso.Monto, factura?.Moneda ?? "MXN", aviso.FechaPago);
                foreach (var email in correos) await correo.EnviarAvisoPagoAsync(email, contenido, cancellationToken);
                aviso.MarcarEnviado(correos.Count, clock.UtcNow);
                await db.SaveChangesAsync(cancellationToken);
                enviados++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "[PortalProveedor] No se pudo enviar el aviso del pago {PagoId}; se reintenta.", aviso.PagoId);
                db.ChangeTracker.Clear();
            }
        }
        return enviados;
    }
}
