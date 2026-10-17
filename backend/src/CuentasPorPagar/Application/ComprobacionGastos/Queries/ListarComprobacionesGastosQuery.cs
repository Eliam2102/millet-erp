using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.ComprobacionGastos;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.ComprobacionGastos.Queries;

public sealed record ListarComprobacionesGastosQuery(
    TipoComprobacionGastos? Tipo = null,
    EstadoComprobacionGastos? Estado = null,
    Guid? SucursalId = null,
    Guid? ResponsableId = null,
    int Offset = 0,
    int Limit = 50,
    string? Q = null) : IRequest<PagedResponse<ComprobacionGastosListItemResponse>>, Millet.SharedKernel.Application.IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_pagar.documentos.leer-todas-sucursales";
    public string TipoDocumento => "comprobacion";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed record ComprobacionGastosListItemResponse(
    Guid Id,
    TipoComprobacionGastos Tipo,
    EstadoComprobacionGastos Estado,
    Guid SucursalId,
    Guid ResponsableId,
    DateOnly FechaInicio,
    DateOnly FechaFin,
    decimal MontoTotal,
    string Moneda,
    int NumeroLineas,
    DateTimeOffset FechaCreacion,
    int Version,
    string? ResponsableNombre = null,
    string? SucursalNombre = null);

public sealed class ListarComprobacionesGastosHandler
    : IRequestHandler<ListarComprobacionesGastosQuery, PagedResponse<ComprobacionGastosListItemResponse>>
{
    private readonly CuentasPorPagarDbContext _db;

    private readonly IEmpleadoReadPort _empleados;
    private readonly ISucursalReadPort _sucursales;

    public ListarComprobacionesGastosHandler(
        CuentasPorPagarDbContext db, IEmpleadoReadPort empleados, ISucursalReadPort sucursales)
    {
        _db = db;
        _empleados = empleados;
        _sucursales = sucursales;
    }

    public async Task<PagedResponse<ComprobacionGastosListItemResponse>> Handle(
        ListarComprobacionesGastosQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.ComprobacionesGastos
            .Where(x => query.DocumentosPermitidos == null || (query.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(x.Id)).AsNoTracking();
        if (query.Tipo is TipoComprobacionGastos t) q = q.Where(c => c.Tipo == t);
        if (query.Estado is EstadoComprobacionGastos e) q = q.Where(c => c.Estado == e);
        if (query.SucursalId is Guid s) q = q.Where(c => c.SucursalId == s);
        if (query.ResponsableId is Guid r) q = q.Where(c => c.ResponsableId == r);

        // q se aplica DESPUÉS del filtro de documentos permitidos (alcance por sucursal): solo acota más.
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var patron = LikePatron.Contiene(query.Q);
            q = q.Where(c =>
                (c.NumeroPedimento != null && EF.Functions.ILike(c.NumeroPedimento, patron, LikePatron.Escape))
                || (c.Observaciones != null && EF.Functions.ILike(c.Observaciones, patron, LikePatron.Escape)));
        }

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderByDescending(c => c.FechaCreacion)
            .Skip(offset)
            .Take(limit)
            .Select(c => new ComprobacionGastosListItemResponse(
                c.Id,
                c.Tipo,
                c.Estado,
                c.SucursalId,
                c.ResponsableId,
                c.FechaInicio,
                c.FechaFin,
                c.MontoTotal,
                c.Moneda,
                c.Lineas.Count(),
                c.FechaCreacion,
                c.Version, null, null))
            .ToListAsync(cancellationToken);

        var responsables = await _empleados.ObtenerPorIdsAsync(
            items.Select(i => i.ResponsableId).Distinct().ToArray(), cancellationToken);
        var sucursales = await _sucursales.ObtenerNombresPorIdsAsync(
            items.Select(i => i.SucursalId).Distinct().ToArray(), cancellationToken);
        items = items.Select(i => i with
        {
            ResponsableNombre = responsables.TryGetValue(i.ResponsableId, out var r) ? r.Nombre : null,
            SucursalNombre = sucursales.GetValueOrDefault(i.SucursalId),
        }).ToList();

        return new PagedResponse<ComprobacionGastosListItemResponse>(items, offset, limit, total);
    }
}
