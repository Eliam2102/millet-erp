namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura hacia DatosMaestros para obtener el master del
/// artículo: UM, conversión, sub-almacén default, tolerancia de
/// recepción (A5 del 01-diseno).
///
/// <para>
/// Lo invocan los handlers de recepción y salida cuando necesitan el
/// dato canónico del artículo (no almacenan copia en Almacén).
/// </para>
/// </summary>
public interface IArticuloReadPort
{
    Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken cancellationToken);

    /// <summary>
    /// Resuelve en <b>batch</b> <c>articuloId → master</c> para enriquecer
    /// DTOs de lectura con clave + descripción sin N+1 (ADR-0042). El detalle
    /// de una salida tiene varias líneas; el handler junta los
    /// <c>ArticuloId</c> distintos y resuelve en una sola consulta. Las claves
    /// no encontradas simplemente no aparecen en el diccionario (el handler
    /// hace fallback al id).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
        IReadOnlyCollection<Guid> articuloIds,
        CancellationToken cancellationToken);
}

public sealed record ArticuloLectura(
    Guid Id,
    string Clave,
    string Descripcion,
    string UnidadMedida,
    decimal? ToleranciaCantidadPorcentaje,
    Guid? SubAlmacenDefaultId,
    bool EsActivo,
    // ADR-0047 PR5.C: precio de referencia del maestro, para el PrecioEstimado de la
    // línea de la RQ automática del motor de reorden. Opcionales (default null) → las
    // construcciones previas de 7 args siguen compilando; el adapter real los proyecta.
    decimal? PrecioReferenciaMonto = null,
    string? PrecioReferenciaMoneda = null);
