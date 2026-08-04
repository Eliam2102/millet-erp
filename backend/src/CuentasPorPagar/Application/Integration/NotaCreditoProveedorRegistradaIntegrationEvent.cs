using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// EventType <c>cuentas_por_pagar.nota-credito.registrada.v1</c>.
/// Compras suscribe para decrementar <c>CantidadFacturada</c> de la
/// OC asociada (§8.1 del 01-diseno).
/// </summary>
public sealed record NotaCreditoProveedorRegistradaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid NotaCreditoId,
    Guid ProveedorId,
    Guid? FacturaOrigenId,
    int TipoRelacionCfdi,
    decimal Total)
    : IntegrationEvent("cuentas_por_pagar.nota-credito.registrada.v1", EmpresaId, OcurridoEn);
