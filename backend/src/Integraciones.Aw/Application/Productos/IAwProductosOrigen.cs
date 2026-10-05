namespace Millet.Integraciones.Aw.Application.Productos;

/// <summary>Variante cruda (medidas nulas != 0).</summary>
public sealed record AwProductoOrigenVariante(
    string? ClaveVariante, decimal? AltoMm, decimal? AnchoMm, decimal? EspesorMm, string? Composicion);

/// <summary>Fila del árbol de composición (<c>BA_STUKL</c>); <c>PadreOrden</c> = posición 1-based de la fila padre, null = raíz.</summary>
public sealed record AwProductoOrigenComponente(
    int Orden, int Nivel, int? PadreOrden, string? Ref, string? Descripcion, string? Tipo, decimal? EspesorMm);

/// <summary>Fila cruda de <c>vw_erp_articulo</c> (doc integration/06 §3) con sus variantes, clasificación y árbol.</summary>
public sealed record AwProductoOrigenFila(
    string? ProductoRef,
    string? Descripcion,
    string? UnidadMedida,
    bool Baja,
    IReadOnlyList<AwProductoOrigenVariante> Variantes,
    DateTime? TransactionTime,
    string? CodigoModelo = null,
    string? Grupo = null,
    string? Tipo = null,
    IReadOnlyList<AwProductoOrigenComponente>? Componentes = null,
    string? Wgr = null,
    string? WgrDescripcion = null);

/// <summary><c>SiguienteCursor</c> = null si ya no hay más páginas.</summary>
public sealed record AwProductosPagina(IReadOnlyList<AwProductoOrigenFila> Filas, string? SiguienteCursor);

/// <summary>Puerto de lectura de productos A+W (solo lectura).</summary>
public interface IAwProductosOrigen
{
    Task<AwProductoOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct);

    /// <summary><paramref name="tamano"/> se acota a 1..<see cref="AwProductosOptions.TamanoLoteMaximo"/>.</summary>
    Task<AwProductosPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct);
}
