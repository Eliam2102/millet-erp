namespace Millet.Integraciones.Aw.Application.Productos;

/// <summary>Variante cruda (medidas nulas != 0).</summary>
public sealed record AwProductoOrigenVariante(
    string? ClaveVariante, decimal? AltoMm, decimal? AnchoMm, decimal? EspesorMm, string? Composicion);

/// <summary>Fila cruda de <c>vw_erp_articulo</c> (doc integration/06 §3) con sus variantes.</summary>
public sealed record AwProductoOrigenFila(
    string? ProductoRef,
    string? Descripcion,
    string? UnidadMedida,
    bool Baja,
    IReadOnlyList<AwProductoOrigenVariante> Variantes,
    DateTime? TransactionTime);

/// <summary><c>SiguienteCursor</c> = null si ya no hay más páginas.</summary>
public sealed record AwProductosPagina(IReadOnlyList<AwProductoOrigenFila> Filas, string? SiguienteCursor);

/// <summary>Puerto de lectura de productos A+W (solo lectura).</summary>
public interface IAwProductosOrigen
{
    Task<AwProductoOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct);

    /// <summary><paramref name="tamano"/> se acota a 1..<see cref="AwProductosOptions.TamanoLoteMaximo"/>.</summary>
    Task<AwProductosPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct);
}
