using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.Application.Tambores;

/// <summary>
/// «Tambores por línea» (M-172): líneas de producción de las sucursales del usuario (ADR-0051) con sus tambores,
/// el activo y el historial (inicio, fin y folio de salida). También alimenta el selector de línea de las salidas.
/// </summary>
public sealed record ListarTamboresLineaQuery(Guid? SucursalId) : IRequest<IReadOnlyList<LineaTamboresDto>>, ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "almacen.salidas.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed record LineaTamboresDto(Guid Id, Guid SucursalId, string Clave, string Nombre, string Proceso,
    IReadOnlyList<TamborDto> Tambores);

public sealed record TamborDto(Guid Id, Guid ArticuloId, string? ArticuloClave, string? ArticuloNombre, string Estado,
    decimal? KgPresentacion, DateTimeOffset FechaInicio, DateTimeOffset? FechaFin, Guid MovimientoId, string Folio,
    string? FolioReemplazo);

public sealed class ListarTamboresLineaHandler(AlmacenDbContext db, IArticuloReadPort articulos)
    : IRequestHandler<ListarTamboresLineaQuery, IReadOnlyList<LineaTamboresDto>>
{
    public async Task<IReadOnlyList<LineaTamboresDto>> Handle(ListarTamboresLineaQuery request, CancellationToken cancellationToken)
    {
        var lineasQ = db.LineasProduccion.AsNoTracking().Where(l => l.Activa);
        if (request.SucursalesPermitidas is { } permitidas) lineasQ = lineasQ.Where(l => permitidas.Contains(l.SucursalId));
        if (request.SucursalId is Guid sucursal) lineasQ = lineasQ.Where(l => l.SucursalId == sucursal);
        var lineas = await lineasQ.OrderBy(l => l.Clave).ToListAsync(cancellationToken);
        var ids = lineas.Select(l => l.Id).ToList();
        // ponytail: historial completo por línea; paginar si una línea junta miles de tambores.
        var tambores = await db.TamboresLinea.AsNoTracking().Where(t => ids.Contains(t.LineaProduccionId))
            .OrderByDescending(t => t.FechaInicio).ThenByDescending(t => t.Id).ToListAsync(cancellationToken);
        var maestro = await articulos.ObtenerPorIdsAsync(tambores.Select(t => t.ArticuloId).Distinct().ToList(), cancellationToken);
        return lineas.Select(l => new LineaTamboresDto(l.Id, l.SucursalId, l.Clave, l.Nombre, l.Proceso,
            tambores.Where(t => t.LineaProduccionId == l.Id).Select(t =>
            {
                var a = maestro.GetValueOrDefault(t.ArticuloId);
                return new TamborDto(t.Id, t.ArticuloId, a?.Clave, a?.Descripcion, t.Estado, t.KgPresentacion, t.FechaInicio,
                    t.FechaFin, t.MovimientoId, t.Folio, t.FolioReemplazo);
            }).ToList())).ToList();
    }
}
