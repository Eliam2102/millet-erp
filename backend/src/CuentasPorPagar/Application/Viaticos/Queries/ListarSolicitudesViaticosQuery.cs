using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Domain.Viaticos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.Viaticos.Queries;

public sealed record ListarSolicitudesViaticosQuery(
    EstadoSolicitudViaticos? Estado = null,
    Guid? EmpleadoId = null,
    Guid? JefeDirectoId = null,
    int Offset = 0,
    int Limit = 50,
    string? Q = null) : IRequest<PagedResponse<SolicitudViaticosListItemResponse>>;

public sealed record SolicitudViaticosListItemResponse(
    Guid Id,
    Guid EmpleadoId,
    Guid JefeDirectoId,
    string Destino,
    DateOnly FechaSalida,
    DateOnly FechaRegreso,
    decimal MontoSolicitado,
    decimal TopePolitica,
    bool ExcedePolitica,
    EstadoSolicitudViaticos Estado,
    decimal? MontoComprobado,
    decimal? DiferenciaLiquidacion,
    DateTimeOffset FechaSolicitud,
    int Version,
    string? EmpleadoNombre = null,
    string? Puesto = null,
    string Moneda = "MXN");

public sealed class ListarSolicitudesViaticosHandler
    : IRequestHandler<ListarSolicitudesViaticosQuery, PagedResponse<SolicitudViaticosListItemResponse>>
{
    private readonly CuentasPorPagarDbContext _db;

    private readonly IEmpleadoReadPort _empleados;
    private readonly IPuestoReadPort _puestos;

    public ListarSolicitudesViaticosHandler(CuentasPorPagarDbContext db, IEmpleadoReadPort empleados, IPuestoReadPort puestos)
    {
        _db = db;
        _empleados = empleados;
        _puestos = puestos;
    }

    public async Task<PagedResponse<SolicitudViaticosListItemResponse>> Handle(
        ListarSolicitudesViaticosQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.SolicitudesViaticos.AsNoTracking();
        if (query.Estado is EstadoSolicitudViaticos e) q = q.Where(s => s.Estado == e);
        if (query.EmpleadoId is Guid emp) q = q.Where(s => s.EmpleadoId == emp);
        if (query.JefeDirectoId is Guid jefe) q = q.Where(s => s.JefeDirectoId == jefe);

        // Esta bandeja no tiene filtro de sucursal propio; q solo acota el conjunto ya visible.
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var patron = LikePatron.Contiene(query.Q);
            q = q.Where(s => EF.Functions.ILike(s.Destino, patron, LikePatron.Escape));
        }

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderByDescending(s => s.FechaSolicitud)
            .Skip(offset)
            .Take(limit)
            .Select(s => new SolicitudViaticosListItemResponse(
                s.Id, s.EmpleadoId, s.JefeDirectoId,
                s.Destino, s.FechaSalida, s.FechaRegreso,
                s.MontoSolicitado, s.TopePoliticaSnapshot, s.ExcedePolitica,
                s.Estado, s.MontoComprobado, s.DiferenciaLiquidacion,
                s.FechaSolicitud, s.Version, null, null, s.Moneda))
            .ToListAsync(cancellationToken);

        var empleados = await _empleados.ObtenerPorIdsAsync(
            items.Select(i => i.EmpleadoId).Distinct().ToArray(), cancellationToken);
        var puestos = (await _puestos.ListarAsync(cancellationToken)).ToDictionary(p => p.Id, p => p.Nombre);
        items = items.Select(i =>
        {
            if (!empleados.TryGetValue(i.EmpleadoId, out var e)) return i;
            return i with
            {
                EmpleadoNombre = e.Nombre,
                Puesto = e.PuestoId is Guid pid ? puestos.GetValueOrDefault(pid) : null,
            };
        }).ToList();

        return new PagedResponse<SolicitudViaticosListItemResponse>(items, offset, limit, total);
    }
}
