using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Administracion.Application.Puestos;

/// <summary>
/// <see cref="SucursalId"/> (F1-ADM-01 Fase 2): filtro opcional que
/// restringe el catálogo a los puestos asignados (vía
/// <c>SucursalPuesto</c> Activo) a esa sucursal. Cuando viene, el
/// handler aplica el mismo guard de pertenencia que
/// <c>ListarPuestosDeSucursalQuery</c> — ver <see cref="SucursalScopeGuard"/>.
/// </summary>
public sealed record ListarPuestosQuery(
    int Offset = 0,
    int Limit = 50,
    string? Query = null,
    EstatusCatalogo? Estatus = null,
    Guid? SucursalId = null)
    : IRequest<ListarPuestosResponse>;

public sealed record ListarPuestosResponse(
    IReadOnlyList<PuestoResponse> Items,
    int Total,
    int Offset,
    int Limit);

public sealed class ListarPuestosHandler
    : IRequestHandler<ListarPuestosQuery, ListarPuestosResponse>
{
    private const int LimitMax = 200;
    private readonly CompartidoDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentUserPermissions _permisos;
    private readonly IUsuarioSucursalReadPort _usuarioSucursal;
    private readonly IRolReadPort _rolReadPort;

    public ListarPuestosHandler(
        CompartidoDbContext db,
        ICurrentUserContext currentUser,
        ICurrentUserPermissions permisos,
        IUsuarioSucursalReadPort usuarioSucursal,
        IRolReadPort rolReadPort)
    {
        _db = db;
        _currentUser = currentUser;
        _permisos = permisos;
        _usuarioSucursal = usuarioSucursal;
        _rolReadPort = rolReadPort;
    }

    public async Task<ListarPuestosResponse> Handle(
        ListarPuestosQuery request,
        CancellationToken cancellationToken)
    {
        var offset = Math.Max(0, request.Offset);
        var limit = request.Limit is <= 0 or > LimitMax ? 50 : request.Limit;
        var query = _db.Puestos.AsNoTracking();

        if (request.Estatus is { } estatus)
        {
            query = query.Where(p => p.Estatus == estatus);
        }

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var text = request.Query.Trim();
            query = query.Where(p => p.Clave.Contains(text) || p.Nombre.Contains(text));
        }

        if (request.SucursalId is { } sucursalId)
        {
            await SucursalScopeGuard.VerificarAsync(
                _currentUser.UserId,
                SucursalScopeGuardPermisos.PuestosGestionar,
                _permisos,
                (userId, ct) => _usuarioSucursal.EstaAsociadoAsync(userId, sucursalId, ct),
                cancellationToken);

            query = query.Where(p => _db.SucursalPuestos.Any(a =>
                a.SucursalId == sucursalId
                && a.PuestoId == p.Id
                && a.Estatus == EstatusCatalogo.Activo));
        }

        var total = await query.CountAsync(cancellationToken);
        var rawItems = await query
            .OrderBy(p => p.Clave)
            .Skip(offset)
            .Take(limit)
            .Select(p => new
            {
                p.Id,
                p.Clave,
                p.Nombre,
                p.Estatus,
                p.Version,
                p.RolSugeridoId,
                p.DepartamentoId
            })
            .ToListAsync(cancellationToken);

        var rolIds = rawItems
            .Where(x => x.RolSugeridoId.HasValue)
            .Select(x => x.RolSugeridoId!.Value)
            .Distinct()
            .ToList();

        var rolNombres = rolIds.Count > 0
            ? await _rolReadPort.ObtenerNombresPorIdsAsync(rolIds, cancellationToken)
            : new Dictionary<Guid, string>();

        var deptoIds = rawItems
            .Where(x => x.DepartamentoId.HasValue)
            .Select(x => x.DepartamentoId!.Value)
            .Distinct()
            .ToList();

        var deptoNombres = deptoIds.Count > 0
            ? await _db.Departamentos.AsNoTracking()
                .Where(d => deptoIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Nombre, cancellationToken)
            : new Dictionary<Guid, string>();

        var items = rawItems.Select(p => new PuestoResponse(
            p.Id,
            p.Clave,
            p.Nombre,
            p.Estatus,
            p.Version,
            p.RolSugeridoId,
            p.RolSugeridoId.HasValue && rolNombres.TryGetValue(p.RolSugeridoId.Value, out var rolNombre)
                ? rolNombre
                : null,
            p.DepartamentoId,
            p.DepartamentoId.HasValue && deptoNombres.TryGetValue(p.DepartamentoId.Value, out var deptoNombre)
                ? deptoNombre
                : null
        )).ToList();

        return new ListarPuestosResponse(items, total, offset, limit);
    }
}
