using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Integration;

/// <summary>
/// EventType <c>almacen.ajuste_inventario.aplicado.v1</c>. Publicado al
/// outbox cuando un <c>ConteoInventario</c> pasa a Aplicado (F7-PR2).
/// Consumidor primario: <b>Contabilidad</b> — genera póliza con cargos
/// y abonos según el signo del ajuste por línea.
///
/// <para>
/// <c>MontoNetoMxn</c> es la suma algebraica (positivos suman, negativos
/// restan). Si > 0 el inventario aumenta (cargo a inventario / abono a
/// resultados de inventario); si &lt; 0 el inventario disminuye (abono
/// a inventario / cargo a resultados).
/// </para>
/// </summary>
public sealed record AjusteInventarioAplicadoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid ConteoId,
    decimal MontoNetoMxn,
    Guid AprobadorId,
    IReadOnlyList<AjusteInventarioPayload> MovimientosGenerados)
    : IntegrationEvent("almacen.ajuste_inventario.aplicado.v1", EmpresaId, OcurridoEn);

public sealed record AjusteInventarioPayload(
    Guid MovimientoId,
    string FolioMovimiento,
    string Tipo, // "AjustePositivo" / "AjusteNegativo"
    Guid SubAlmacenId,
    // C7.2c: el ajuste de conteo ahora golpea un rack específico. Contabilidad
    // lo querrá para la póliza a nivel ubicación.
    Guid UbicacionId,
    Guid ArticuloId,
    decimal CantidadAjustada,
    decimal CostoUnitarioMxn,
    decimal MontoMxn);
