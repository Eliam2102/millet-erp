using MediatR;

namespace Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;

/// <summary>
/// Evento in-process emitido cuando la conciliación con OC pasa
/// dentro de tolerancia **pero** hay diferencia de precio unitario en
/// una línea — caso "variante B" (materiales directos, §11.6 del
/// 00-levantamiento, §3.bis.5).
///
/// <para>
/// Se emite UNO POR ARTÍCULO con diferencia (forma plana), espejando el
/// payload que el <c>CxpEventListenerWorker</c> de Almacén deserializa
/// (<c>DiferenciaPrecioFacturaDetectadaPayload</c> en
/// <c>Almacen/Application/EventListeners/CxpContracts.cs</c>) — la forma
/// anterior con lista <c>PorLinea[]</c> nunca se emitió y NO coincidía
/// con el consumidor (GAP-3 de la verificación e2e 2026-07-15). Almacén
/// re-valoriza el remanente en stock; Compras lo recibe informativo.
/// </para>
/// </summary>
public sealed record DiferenciaPrecioFacturaDetectadaDomainEvent(
    Guid EmpresaId,
    Guid FacturaProveedorId,
    Guid OrdenCompraId,
    Guid ArticuloId,
    decimal CantidadFacturada,
    decimal PrecioFacturaUnitarioMxn,
    decimal PrecioOcUnitarioMxn,
    decimal DiferenciaUnitarioMxn,
    decimal MontoDiferenciaTotalMxn,
    DateTimeOffset OcurridoEn) : INotification;
