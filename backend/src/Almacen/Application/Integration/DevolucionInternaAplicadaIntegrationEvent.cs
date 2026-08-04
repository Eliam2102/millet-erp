using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Integration;

/// <summary>
/// EventType <c>almacen.devolucion_interna.aplicada.v1</c>. Publicado
/// cuando una devolución interna (sub-flujo 8.A) pasa a estado
/// Registrado (F5-PR1). Consumidor primario: <b>Contabilidad</b> —
/// genera póliza inversa al gasto del consumo original.
///
/// <para>
/// <see cref="EstadoMaterial"/>: <c>"Integro"</c>, <c>"UsadoParcial"</c>
/// o <c>"Danado"</c>. Si es <c>Danado</c>, el material va al sub-almacén
/// <c>MAT-REV</c> (A15) y aguarda decisión de Calidad
/// (<c>BajaPorDano</c> o <c>ReincorporacionTrasRevision</c>).
/// </para>
/// </summary>
public sealed record DevolucionInternaAplicadaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid DevolucionId,
    string FolioDevolucion,
    Guid SalidaOrigenId,
    Guid SubAlmacenDestinoId,
    string EstadoMaterial,
    decimal CostoTotalRevertidoMxn,
    IReadOnlyList<LineaDevolucionPayload> Lineas)
    : IntegrationEvent("almacen.devolucion_interna.aplicada.v1", EmpresaId, OcurridoEn);

public sealed record LineaDevolucionPayload(
    Guid LineaDevolucionId,
    Guid ArticuloId,
    string UnidadMedida,
    decimal Cantidad,
    decimal CostoUnitarioMxn,
    decimal MontoTotalMxn);
