using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>EventType <c>cuentas_por_pagar.anticipo.capturado.v1</c>.</summary>
public sealed record AnticipoProveedorCapturadoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid AnticipoId,
    Guid ProveedorId,
    decimal MontoEntregado,
    Guid? OrdenCompraId,
    // G1.6: opcionales al final.
    Guid? CuentaBancariaId = null,
    string? Moneda = null,
    decimal? TipoCambio = null)
    : IntegrationEvent("cuentas_por_pagar.anticipo.capturado.v1", EmpresaId, OcurridoEn);
