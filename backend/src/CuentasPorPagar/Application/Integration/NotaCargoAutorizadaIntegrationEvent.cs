using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>EventType <c>cuentas_por_pagar.nota-cargo.autorizada.v1</c>.</summary>
public sealed record NotaCargoAutorizadaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid NotaCargoId,
    Guid ProveedorId,
    decimal Monto,
    Guid? FacturaOrigenId)
    : IntegrationEvent("cuentas_por_pagar.nota-cargo.autorizada.v1", EmpresaId, OcurridoEn);
