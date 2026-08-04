using MediatR;

namespace Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.Events;

/// <summary>
/// Evento in-process emitido al capturar una NC en estado
/// <c>Abierta</c> o <c>EnEspera</c>. **Compras** suscribe el
/// integration event para decrementar <c>CantidadFacturada</c> cuando
/// la NC sea relación tipo 01/03/07 (§8.1 del 01-diseno).
///
/// <para>
/// Si la NC nace EnEspera (sin factura origen), el evento se emite
/// igual con <see cref="FacturaOrigenId"/> = null; Compras lo ignora
/// hasta que el worker vincule y se publique de nuevo. F6-PR1
/// publica el primer evento; F6-PR2+ extiende para emitir tras
/// vinculación.
/// </para>
/// </summary>
public sealed record NotaCreditoProveedorRegistradaDomainEvent(
    Guid EmpresaId,
    Guid NotaCreditoId,
    Guid ProveedorId,
    Guid? FacturaOrigenId,
    int TipoRelacionCfdi,
    decimal Total,
    DateTimeOffset OcurridoEn) : INotification;
