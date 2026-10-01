
namespace Millet.DatosMaestros.Application.ProductosAw;

public sealed record ProductoAwVarianteContratoV1(
    string ClaveVariante, decimal? AltoMm, decimal? AnchoMm, decimal? EspesorMm, string? Composicion);

/// <summary>
/// Contrato de lectura estable del producto A+W (doc integration/06 §9). Cambios aditivos no suben
/// <see cref="VersionContrato"/>; los incompatibles sí. Nulo = no informado (nunca 0).
/// </summary>
public sealed record ProductoAwContratoV1(
    string VersionContrato,
    Guid Id,
    string Referencia,
    string Descripcion,
    string Unidad,
    string? ClaveProdServSat,
    string? ClaveUnidadSat,
    string? ObjetoImp,
    decimal? TasaIvaTraslado,
    decimal? TasaRetencionIva,
    decimal? TasaRetencionIsr,
    string Estatus,
    DateTime? FechaBaja,
    IReadOnlyList<ProductoAwVarianteContratoV1> Variantes,
    int Version)
{
    public const string VersionActual = "1";
}

/// <summary>Puerto para consumidores (inventario, producción, facturación) sin tocar tablas. Devuelve inactivos con su estatus; nunca borra.</summary>
public interface IProductoAwContratoReadPort
{
    Task<ProductoAwContratoV1?> ObtenerPorReferenciaAsync(string referencia, CancellationToken ct);
    Task<ProductoAwContratoV1?> ObtenerPorIdAsync(Guid id, CancellationToken ct);
}
