using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Integration;

/// <summary>
/// EventType <c>almacen.recepcion_activo.anulada.v1</c>. Una recepción de activos fijos se
/// anuló antes de capitalizarse (espejo del 102 de SAP), una fila por línea de OC.
/// Consumidores: ActivosFijos da de baja los activos «por capitalizar» de la línea;
/// Compras fija <c>CantidadRecibida</c> al acumulado ya calculado aquí.
/// Idempotencia por <see cref="RecepcionActivoId"/>.
/// </summary>
public sealed record RecepcionActivoAnuladaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid RecepcionActivoId,
    Guid RecepcionId,
    string? FolioRecepcion,
    Guid OrdenCompraId,
    Guid LineaOcId,
    decimal Cantidad,
    // Recibido neto de la línea de OC tras la anulación, en unidad de la OC, calculado por Almacén.
    decimal CantidadRecibidaAcumulada,
    string Motivo)
    : IntegrationEvent("almacen.recepcion_activo.anulada.v1", EmpresaId, OcurridoEn);
