using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// EventType <c>cuentas_por_pagar.estado-cuenta-tc.cerrado.v1</c>
/// (§9.1 anexo TC, F7-PR6). Consumidores: Tesorería (vía la
/// FacturaProveedor con <c>PasivoAutorizadoParaPagoEvent</c>) y
/// Contabilidad.
/// </summary>
public sealed record EstadoCuentaTcCerradoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid EstadoCuentaTcId,
    Guid TarjetaId,
    decimal TotalBancoMxn,
    Guid FacturaProveedorId,
    decimal? DiferenciaCambiariaMxn,
    // C1.5b (aditivo): banco emisor (proveedor de la factura agregada) y sucursal del titular, para la póliza de Contabilidad.
    Guid? ProveedorId = null,
    Guid? SucursalId = null)
    : IntegrationEvent("cuentas_por_pagar.estado-cuenta-tc.cerrado.v1", EmpresaId, OcurridoEn);
