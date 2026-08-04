using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// EventType <c>cuentas_por_pagar.factura.rechazada-por-tolerancia.v1</c>.
/// Compras suscribe para regresar la OC a revisión / corrección
/// (§8.1 del 01-diseno).
/// </summary>
public sealed record FacturaProveedorRechazadaPorToleranciaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    Guid OrdenCompraId,
    decimal TotalFactura,
    decimal TotalOc,
    decimal Diferencia,
    string ToleranciaAplicada)
    : IntegrationEvent("cuentas_por_pagar.factura.rechazada-por-tolerancia.v1", EmpresaId, OcurridoEn);
