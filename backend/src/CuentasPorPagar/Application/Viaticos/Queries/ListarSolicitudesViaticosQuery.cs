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
    string? Q = null) : IRequest<PagedResponse<SolicitudViaticosListItemResponse>>, Millet.SharedKernel.Application.IDocumentoScopedQuery
{
    // ADR-0051: la sucursal de una solicitud es la del empleado (CxpSucursalReadAdapter).
    public string PermisoTodasSucursales => "cuentas_por_pagar.documentos.leer-todas-sucursales";
    public string TipoDocumento => ViaticosAlcance.TipoDocumento;
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
    /// <summary>Empleado del usuario: ve sus solicitudes y las de sus subordinados aunque sean de otra sucursal.</summary>
    public Guid? MiEmpleadoId { get; set; }
}

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

/// <summary>
/// Filtros de la bandeja salvo el estado, compartidos por el listado y por los conteos
/// por estado (UX-F3-05) para que ambos coincidan siempre.
/// </summary>
internal static class ViaticosFiltro
{
    public static IQueryable<Millet.CuentasPorPagar.Domain.Viaticos.SolicitudViaticos> Aplicar(CuentasPorPagarDbContext db, ListarSolicitudesViaticosQuery request)
    {
        var q = db.SolicitudesViaticos.AsNoTracking();
        if (request.EmpleadoId is Guid emp) q = q.Where(s => s.EmpleadoId == emp);
        if (request.JefeDirectoId is Guid jefe) q = q.Where(s => s.JefeDirectoId == jefe);

        if (request.DocumentosPermitidos is { } permitidos)
        {
            var mio = request.MiEmpleadoId;
            q = q.Where(s => permitidos.Contains(s.Id) || (mio != null && (s.EmpleadoId == mio || s.JefeDirectoId == mio)));
        }

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var patron = LikePatron.Contiene(request.Q);
            q = q.Where(s => EF.Functions.ILike(s.Destino, patron, LikePatron.Escape));
        }

        return q;
    }
}

public sealed record ConteosViaticosPorEstadoQuery(Guid? EmpleadoId, Guid? JefeDirectoId, string? Q = null)
    : IRequest<IReadOnlyList<Millet.SharedKernel.Application.ConteoPorEstadoItem>>, Millet.SharedKernel.Application.IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_pagar.documentos.leer-todas-sucursales";
    public string TipoDocumento => ViaticosAlcance.TipoDocumento;
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class ConteosViaticosPorEstadoHandler(
    CuentasPorPagarDbContext db, IEmpleadoReadPort empleados, Millet.SharedKernel.Application.ICurrentUserContext usuario)
    : IRequestHandler<ConteosViaticosPorEstadoQuery, IReadOnlyList<Millet.SharedKernel.Application.ConteoPorEstadoItem>>
{
    public async Task<IReadOnlyList<Millet.SharedKernel.Application.ConteoPorEstadoItem>> Handle(
        ConteosViaticosPorEstadoQuery request, CancellationToken cancellationToken)
    {
        var lista = new ListarSolicitudesViaticosQuery(null, request.EmpleadoId, request.JefeDirectoId, 0, 1, request.Q)
        {
            DocumentosPermitidos = request.DocumentosPermitidos,
            MiEmpleadoId = await ViaticosAlcance.MiEmpleadoAsync(empleados, usuario, cancellationToken),
        };
        return await ViaticosFiltro.Aplicar(db, lista).GroupBy(x => x.Estado)
            .Select(g => new Millet.SharedKernel.Application.ConteoPorEstadoItem((int)g.Key, g.Count()))
            .ToListAsync(cancellationToken);
    }
}

public sealed class ListarSolicitudesViaticosHandler
    : IRequestHandler<ListarSolicitudesViaticosQuery, PagedResponse<SolicitudViaticosListItemResponse>>
{
    private readonly CuentasPorPagarDbContext _db;

    private readonly IEmpleadoReadPort _empleados;
    private readonly IPuestoReadPort _puestos;

    private readonly Millet.SharedKernel.Application.ICurrentUserContext _usuario;

    public ListarSolicitudesViaticosHandler(CuentasPorPagarDbContext db, IEmpleadoReadPort empleados, IPuestoReadPort puestos,
        Millet.SharedKernel.Application.ICurrentUserContext usuario)
    {
        _db = db;
        _empleados = empleados;
        _puestos = puestos;
        _usuario = usuario;
    }

    public async Task<PagedResponse<SolicitudViaticosListItemResponse>> Handle(
        ListarSolicitudesViaticosQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        if (query.DocumentosPermitidos is not null)
            query.MiEmpleadoId = await ViaticosAlcance.MiEmpleadoAsync(_empleados, _usuario, cancellationToken);
        var q = ViaticosFiltro.Aplicar(_db, query);
        if (query.Estado is EstadoSolicitudViaticos e) q = q.Where(s => s.Estado == e);

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

/// <summary>Alcance por sucursal de viáticos (ADR-0051) compartido por listado, conteos y detalle.</summary>
public static class ViaticosAlcance
{
    public const string TipoDocumento = "solicitud_viaticos";

    public static async Task<Guid?> MiEmpleadoAsync(
        IEmpleadoReadPort empleados, Millet.SharedKernel.Application.ICurrentUserContext usuario, CancellationToken ct)
        => usuario.UserId is Guid id ? (await empleados.ObtenerPorUsuarioAsync(id, ct))?.Id : null;
}
