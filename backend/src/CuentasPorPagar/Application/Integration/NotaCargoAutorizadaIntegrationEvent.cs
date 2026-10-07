using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>EventType <c>cuentas_por_pagar.nota-cargo.autorizada.v1</c>.</summary>
public sealed record NotaCargoAutorizadaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid NotaCargoId,
    Guid ProveedorId,
    decimal Monto,
    Guid? FacturaOrigenId,
    // G1.6: bloque contable opcional (al final; JSON antiguo sin estos campos deserializa a null).
    string? Uuid = null,
    decimal? Subtotal = null,
    decimal? Iva = null,
    decimal? RetencionesTotal = null,
    IReadOnlyList<RetencionDetallePayload>? Retenciones = null,
    string? Moneda = null,
    decimal? TipoCambio = null,
    Guid? SucursalId = null)
    : IntegrationEvent("cuentas_por_pagar.nota-cargo.autorizada.v1", EmpresaId, OcurridoEn);
