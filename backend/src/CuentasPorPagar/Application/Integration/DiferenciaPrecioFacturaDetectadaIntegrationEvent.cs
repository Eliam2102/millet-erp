using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// EventType <c>cuentas_por_pagar.factura.diferencia-precio-detectada.v1</c>.
/// Publicado al outbox cuando la captura de factura contra OC pasa
/// dentro de tolerancia pero una línea trae precio unitario distinto al
/// de la OC y la OC tiene recepción variante B (factura pendiente).
/// Uno por artículo con diferencia — forma plana que espeja EXACTAMENTE
/// el payload del consumidor
/// (<c>Almacen/Application/EventListeners/CxpContracts.cs</c>): Almacén
/// re-valoriza el remanente en stock
/// (<c>montoAjuste = remanente × DiferenciaUnitarioMxn</c>); Compras lo
/// recibe informativo. GAP-3 de la verificación e2e 2026-07-15.
/// </summary>
public sealed record DiferenciaPrecioFacturaDetectadaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    Guid OrdenCompraId,
    Guid ArticuloId,
    decimal CantidadFacturada,
    decimal PrecioFacturaUnitarioMxn,
    decimal PrecioOcUnitarioMxn,
    decimal DiferenciaUnitarioMxn,
    decimal MontoDiferenciaTotalMxn)
    : IntegrationEvent("cuentas_por_pagar.factura.diferencia-precio-detectada.v1", EmpresaId, OcurridoEn);
