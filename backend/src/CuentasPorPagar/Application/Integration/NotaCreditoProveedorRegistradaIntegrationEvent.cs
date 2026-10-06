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
    decimal Total,
    // G1.6: bloque contable opcional (al final; JSON antiguo sin estos campos deserializa a null).
    string? Uuid = null,
    decimal? Subtotal = null,
    decimal? Iva = null,
    decimal? Retenciones = null,
    IReadOnlyList<RetencionDetallePayload>? RetencionesDetalle = null,
    string? Moneda = null,
    decimal? TipoCambio = null,
    Guid? SucursalId = null)
    : IntegrationEvent("cuentas_por_pagar.nota-credito.registrada.v1", EmpresaId, OcurridoEn);
