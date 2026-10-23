using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Integration;

/// <summary>
/// EventType <c>almacen.recepcion_oc.cancelada.v1</c> (86e3tabdp). Publicado al outbox cuando se cancela con motivo
/// una recepción mal capturada (el inventario ya se revirtió con movimientos CEN). Consumidores:
/// <list type="bullet">
///   <item><b>Compras</b>: devuelve a pendiente la cantidad recibida de cada línea de OC.</item>
///   <item><b>CxP</b>: borra la proyección de la recepción y desliga las facturas que la habían elegido (sin nota de cargo).</item>
/// </list>
/// Idempotencia en consumidores por <see cref="RecepcionId"/> (id de la recepción principal).
/// </summary>
public sealed record OcRecepcionCanceladaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid RecepcionId,
    string FolioRecepcion,
    Guid OrdenCompraId,
    DateOnly FechaMovimiento,
    string MotivoCodigo,
    string MotivoTexto,
    Guid CanceladaPor,
    IReadOnlyList<string> FoliosCancelacion,
    IReadOnlyList<LineaRecepcionCanceladaPayload> Lineas,
    Guid? AlmacenId = null,
    Guid? SucursalId = null)
    : IntegrationEvent("almacen.recepcion_oc.cancelada.v1", EmpresaId, OcurridoEn);

/// <summary>
/// Línea revertida. <c>Cantidad</c> va en unidad base del inventario; <c>CantidadDocumento</c> en unidad de la línea
/// de OC (la que Compras descuenta de lo recibido). Sin <c>LineaOcId</c> (recepciones históricas) no hay descuento por línea.
/// </summary>
public sealed record LineaRecepcionCanceladaPayload(
    Guid LineaRecepcionId,
    Guid? LineaOcId,
    Guid ArticuloId,
    string UnidadMedida,
    decimal Cantidad,
    decimal CantidadDocumento,
    decimal CostoUnitarioMxn,
    decimal MontoTotalMxn,
    Guid UbicacionId);
