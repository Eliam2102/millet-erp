using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// EventType <c>cuentas_por_pagar.nc-fiscal-devolucion.recibida.v1</c>
/// (F6-PR3). Publicado al outbox cuando se captura una NC del proveedor
/// con <c>TipoRelacionCfdi=03</c> (Devolución) que cierra una
/// <c>NotaCargo</c> generada por una devolución a proveedor de Almacén.
/// Almacén lo consume para marcar la <c>DevolucionAProveedor</c> con
/// <c>ConciliadaConNcFiscal=true</c>, cerrando el ciclo bidireccional.
/// </summary>
public sealed record NotaCreditoFiscalDevolucionRecibidaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid NotaCreditoProveedorId,
    Guid NotaCargoId,
    Guid DevolucionAProveedorId,
    Guid ProveedorId,
    decimal Total,
    string UuidCfdi)
    : IntegrationEvent("cuentas_por_pagar.nc-fiscal-devolucion.recibida.v1", EmpresaId, OcurridoEn);
