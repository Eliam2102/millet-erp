using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Integration;

/// <summary>
/// EventType <c>almacen.entrada_inventario.valorada.v1</c>. Publicado al
/// outbox cuando una recepción se valora completamente (variante A al
/// registrar; variante B al conciliar con factura o aplicar ajuste de
/// precio A11). Consumidor primario: <b>Contabilidad</b> — genera la
/// póliza correspondiente.
///
/// <para>
/// El campo <see cref="MontoTotalMxn"/> refleja el costo VALORADO final
/// (puede diferir del registrado en variante A si llegó ajuste posterior
/// de precio). El campo <see cref="EsAjuste"/> distingue:
/// <list type="bullet">
///   <item><c>false</c>: valoración inicial al registrar recepción.</item>
///   <item><c>true</c>: re-valoración tras
///   <c>DiferenciaPrecioFacturaDetectada</c> (A11) — se acompaña de un
///   movimiento <c>AjustePrecioFactura</c>.</item>
/// </list>
/// </para>
/// </summary>
public sealed record EntradaInventarioValoradaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid RecepcionId,
    Guid OrdenCompraId,
    decimal MontoTotalMxn,
    bool EsAjuste,
    string ConceptoContableSugerido,
    IReadOnlyList<LineaValoradaPayload> Lineas)
    : IntegrationEvent("almacen.entrada_inventario.valorada.v1", EmpresaId, OcurridoEn);

public sealed record LineaValoradaPayload(
    Guid LineaRecepcionId,
    Guid ArticuloId,
    decimal Cantidad,
    decimal CostoUnitarioMxn,
    decimal MontoLineaMxn);
