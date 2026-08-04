namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Puerto de lectura del master de Producto (dueño: <c>DatosMaestros</c>).
/// Facturación resuelve la clave SAT, clave unidad SAT, objeto de impuesto,
/// tasas de IVA/retención y el atributo <c>origen</c> por artículo. Sin clave
/// SAT no se factura (§16.12 levantamiento).
/// </summary>
public interface IProductosReadPort
{
    /// <summary>Resuelve un producto por su id interno del ERP.</summary>
    Task<ProductoFiscalLectura?> ObtenerAsync(Guid productoId, CancellationToken cancellationToken);

    /// <summary>
    /// Resuelve un producto por su referencia externa (p.ej. <c>producto_id</c>
    /// de A+W). Devuelve <c>null</c> si no existe en el master.
    /// </summary>
    Task<ProductoFiscalLectura?> ResolverPorReferenciaAsync(
        string referenciaExterna,
        CancellationToken cancellationToken);

    /// <summary>
    /// Búsqueda de productos activos para el selector de conceptos en emisión
    /// (FAC-UX-PR1, cierra PLATFORM-TODO(&lt;ProductoSelector&gt;)). Filtros
    /// excluyentes al estilo ADR-0045: referencia externa (substring) o
    /// descripción (substring con folding de acentos). Sin filtros devuelve
    /// los primeros <paramref name="limit"/> por referencia.
    /// </summary>
    Task<IReadOnlyList<ProductoAwBusquedaItem>> BuscarAsync(
        string? referencia,
        string? descripcion,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>
/// Item de búsqueda para el selector de producto en emisión: incluye la
/// referencia A+W (display) y los atributos fiscales para autollenar el
/// concepto al seleccionar.
/// </summary>
public sealed record ProductoAwBusquedaItem(
    Guid ProductoId,
    string ReferenciaExterna,
    string Descripcion,
    string UnidadMedida,
    string? ClaveProdServSat,
    string? ClaveUnidadSat,
    string? ObjetoImp,
    decimal? TasaIvaTraslado,
    decimal? TasaRetencionIva,
    decimal? TasaRetencionIsr,
    string? FraccionArancelaria,
    string? UnidadAduana,
    decimal? PesoUnitarioKg);

/// <summary>
/// Snapshot de los datos fiscales del producto/servicio. <c>requiere_pedimento</c>
/// <b>no</b> vive aquí — lo aporta el pedido (línea o cabecera), porque el mismo
/// artículo puede venderse con o sin pedimento (§3.bis.5 diseño).
/// </summary>
public sealed record ProductoFiscalLectura(
    Guid ProductoId,
    string Descripcion,
    string? ClaveProdServSat,
    string? ClaveUnidadSat,
    string? ObjetoImp,
    decimal? TasaIvaTraslado,
    decimal? TasaRetencionIva,
    decimal? TasaRetencionIsr,
    string Origen);
