using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// EventType <c>cuentas_por_pagar.factura.autorizada.v1</c>. Informativo
/// para Compras + Contabilidad. Tesorería publica
/// <c>PasivoAutorizadoParaPagoEvent</c> aparte cuando los datos bancarios
/// están completos (F9-PR1).
/// </summary>
public sealed record FacturaProveedorAutorizadaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid FacturaProveedorId,
    Guid? OrdenCompraId)
    : IntegrationEvent("cuentas_por_pagar.factura.autorizada.v1", EmpresaId, OcurridoEn);
