using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.Reportes;

/// <summary>
/// Reporte <b>SAP-REPORTE-EXISTENCIA-MP-CNK</b> (F8-PR1). Inventario
/// diario de materiales directos (variante B): listado de saldos
/// vigentes con stock > 0 en sub-almacenes tipo MaterialesDirectos.
/// </summary>
public sealed record ExistenciaMpCnkQuery(
    Guid? SubAlmacenId) : IRequest<ReporteResponse<ExistenciaMpCnkFila>>;

public sealed record ExistenciaMpCnkFila(
    Guid SubAlmacenId,
    Guid ArticuloId,
    decimal Cantidad,
    decimal CantidadDisponible,
    decimal CostoPromedioMxn,
    decimal ValorInventarioMxn,
    // Resueltos en backend (ADR-0042). Null = el read-port no resolvió → el
    // frontend cae al id crudo. SubAlmacenNombre viene por JOIN; Articulo* por batch.
    string? SubAlmacenNombre,
    string? ArticuloClave,
    string? ArticuloDescripcion);

public sealed class ExistenciaMpCnkHandler
    : IRequestHandler<ExistenciaMpCnkQuery, ReporteResponse<ExistenciaMpCnkFila>>
{
    private readonly AlmacenDbContext _db;
    private readonly IArticuloReadPort _articulos;

    public ExistenciaMpCnkHandler(AlmacenDbContext db, IArticuloReadPort articulos)
    {
        _db = db;
        _articulos = articulos;
    }

    /// <summary>
    /// Columnas del reporte. La <c>Clave</c> coincide (camelCase) con la key
    /// serializada de <see cref="ExistenciaMpCnkFila"/> — el front
    /// (<c>ReporteShell</c>) resuelve <c>fila[clave]</c> sensible a mayúsculas,
    /// así que un desalineo deja la celda en blanco. Se muestran nombre/clave/
    /// descripción (no los GUID crudos de sub-almacén/artículo).
    /// </summary>
    public static readonly IReadOnlyList<ColumnaDescriptor> Columnas =
    [
        new("subAlmacenNombre", "Sub-almacén", "texto"),
        new("articuloClave", "Artículo", "texto"),
        new("articuloDescripcion", "Descripción", "texto"),
        new("cantidad", "Cantidad", "numero", "right"),
        new("cantidadDisponible", "Disponible", "numero", "right"),
        new("costoPromedioMxn", "Costo prom.", "moneda", "right"),
        new("valorInventarioMxn", "Valor", "moneda", "right"),
    ];

    /// <summary>Totales del pie. Las keys deben ser claves de columna (el front
    /// pinta el total bajo la columna cuya <c>clave</c> matchea la key).</summary>
    public static Dictionary<string, decimal> CalcularTotales(
        IReadOnlyList<ExistenciaMpCnkFila> filas) => new()
    {
        ["cantidad"] = filas.Sum(f => f.Cantidad),
        ["valorInventarioMxn"] = filas.Sum(f => f.ValorInventarioMxn),
    };

    /// <summary>ArticuloId distintos para el batch de enriquecimiento (ADR-0042).</summary>
    public static IReadOnlyCollection<Guid> ExtraerArticuloIdsDistintos(
        IReadOnlyList<ExistenciaMpCnkFila> filas)
        => filas.Select(f => f.ArticuloId).Distinct().ToArray();

    /// <summary>Aplica clave/descripción del artículo resuelto; fallback a null
    /// (el FE cae al id) cuando el read-port no lo trae.</summary>
    public static List<ExistenciaMpCnkFila> AplicarArticulos(
        IReadOnlyList<ExistenciaMpCnkFila> filas,
        IReadOnlyDictionary<Guid, ArticuloLectura> articulos)
        => filas.Select(f => articulos.TryGetValue(f.ArticuloId, out var art)
            ? f with { ArticuloClave = art.Clave, ArticuloDescripcion = art.Descripcion }
            : f).ToList();

    public async Task<ReporteResponse<ExistenciaMpCnkFila>> Handle(
        ExistenciaMpCnkQuery request, CancellationToken cancellationToken)
    {
        // C7.2a: agregado por (sub-almacén, artículo) — con N bins habría
        // filas duplicadas por artículo (el grano del reporte es (sub, art)).
        // Puente determinista: SUM + costo ponderado. El grano por-ubicación
        // se difiere a C7.2b con su decisión de UX. Ordenar y agrupar sobre
        // columnas de ORIGEN (traducible a SQL); el record se materializa
        // después.
        var query = (from s in _db.SaldosInventario.AsNoTracking()
                     join sa in _db.SubAlmacenes.AsNoTracking() on s.SubAlmacenId equals sa.Id
                     where sa.Tipo == TipoSubAlmacen.MaterialesDirectos
                        && s.Cantidad > 0
                        && (request.SubAlmacenId == null || s.SubAlmacenId == request.SubAlmacenId)
                     select new { s.SubAlmacenId, s.ArticuloId, s.Cantidad, s.CantidadDisponible, s.ValorInventarioMxn, SubAlmacenNombre = sa.Nombre })
            .GroupBy(x => new { x.SubAlmacenId, x.ArticuloId, x.SubAlmacenNombre })
            .Select(g => new
            {
                g.Key.SubAlmacenId,
                g.Key.ArticuloId,
                g.Key.SubAlmacenNombre,
                Cantidad = g.Sum(x => x.Cantidad),
                CantidadDisponible = g.Sum(x => x.CantidadDisponible),
                ValorInventarioMxn = g.Sum(x => x.ValorInventarioMxn),
            })
            .OrderBy(x => x.SubAlmacenId).ThenBy(x => x.ArticuloId);

        var crudas = await query.ToListAsync(cancellationToken);
        var filas = crudas
            .Select(x => new ExistenciaMpCnkFila(
                x.SubAlmacenId, x.ArticuloId,
                x.Cantidad, x.CantidadDisponible,
                x.Cantidad > 0 ? Math.Round(x.ValorInventarioMxn / x.Cantidad, 4) : 0m,
                x.ValorInventarioMxn,
                // Articulo* nulos aquí, se enriquecen en batch abajo.
                x.SubAlmacenNombre, null, null))
            .ToList();

        // Enriquecimiento de artículo (clave/descripción) en batch sobre los
        // ArticuloId distintos (ADR-0042, anti-N+1); fallback a null cuando el
        // read-port no resuelve. El sub-almacén ya vino por JOIN (sa.Nombre).
        var ids = ExtraerArticuloIdsDistintos(filas);
        var articulos = ids.Count > 0
            ? await _articulos.ObtenerPorIdsAsync(ids, cancellationToken)
            : new Dictionary<Guid, ArticuloLectura>();
        filas = AplicarArticulos(filas, articulos);

        return new ReporteResponse<ExistenciaMpCnkFila>(
            Titulo: "SAP-REPORTE-EXISTENCIA-MP-CNK",
            GeneradoEn: DateTimeOffset.UtcNow,
            FiltrosAplicados: new Dictionary<string, string?>
            {
                ["sub_almacen_id"] = request.SubAlmacenId?.ToString(),
            },
            Columnas: Columnas,
            Filas: filas,
            Totales: CalcularTotales(filas));
    }
}
