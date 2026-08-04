using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// EventType <c>cuentas_por_pagar.factura.cancelada.v1</c>. Compras
/// suscribe para decrementar <c>CantidadFacturada</c> (§8.1 del
/// 01-diseno). **NO** se publica si el motivo es
/// <c>RechazadaPorTolerancia</c> (ese caso usa
/// <see cref="FacturaProveedorRechazadaPorToleranciaIntegrationEvent"/>).
/// </summary>
public sealed record FacturaProveedorCanceladaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    Guid? OrdenCompraId,
    string Motivo,
    string? MotivoTexto)
    : IntegrationEvent("cuentas_por_pagar.factura.cancelada.v1", EmpresaId, OcurridoEn);
