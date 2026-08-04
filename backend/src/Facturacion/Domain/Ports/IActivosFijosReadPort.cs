namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Puerto de lectura del catálogo de Activos Fijos (§7.8 levantamiento). Valida
/// que un producto esté dado de alta como activo fijo, con su valor en libros y
/// depreciación acumulada (para los asientos de baja). Dueño real: el módulo
/// <c>Activos Fijos</c>; en dev es stub vacío
/// (<c>PLATFORM-TODO(&lt;ActivosFijos&gt;)</c>) — sin ese módulo no se pueden
/// vender activos.
/// </summary>
public interface IActivosFijosReadPort
{
    /// <summary>Resuelve un activo fijo por su referencia; <c>null</c> si no está dado de alta.</summary>
    Task<ActivoFijoLectura?> ObtenerAsync(string activoRef, CancellationToken cancellationToken);
}

/// <summary>Snapshot de un activo fijo para autorizar su venta (§7.8).</summary>
public sealed record ActivoFijoLectura(
    string ActivoRef,
    string Descripcion,
    decimal ValorEnLibros,
    decimal DepreciacionAcumulada,
    bool EsImportacion);
