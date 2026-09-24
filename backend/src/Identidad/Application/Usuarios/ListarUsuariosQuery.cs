using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Identidad.Application.Usuarios;

/// <summary>
/// Lista paginada de usuarios con shape admin completo (F-Admin-PR4.2).
///
/// <list type="bullet">
///   <item><see cref="SoloActivos"/> = <c>true</c> filtra <c>Activo</c>.</item>
///   <item><see cref="EmpresaId"/> filtra usuarios con al menos una
///         asignación en esa empresa.</item>
///   <item><see cref="RolId"/> filtra usuarios con al menos una asignación
///         a ese rol (en cualquier empresa).</item>
/// </list>
///
/// Distinto del catálogo <c>UsuariosEndpoints.MapGet("/")</c> que devuelve
/// el shape <c>UsuarioListItem</c> sin <c>EntraOid</c> para selectores de UI.
/// </summary>
public sealed record ListarUsuariosQuery(
    int Offset = 0,
    int Limit = 50,
    bool? SoloActivos = null,
    Guid? EmpresaId = null,
    Guid? RolId = null) : IRequest<ListarUsuariosResponse>;

public sealed class ListarUsuariosHandler
    : IRequestHandler<ListarUsuariosQuery, ListarUsuariosResponse>
{
    private const int LimitMax = 200;
    private readonly IdentidadDbContext _db;
    private readonly CompartidoDbContext _compartido;
    private readonly ICurrentEmpresaContext _empresaContext;

    public ListarUsuariosHandler(
        IdentidadDbContext db,
        CompartidoDbContext compartido,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _compartido = compartido;
        _empresaContext = empresaContext;
    }

    public async Task<ListarUsuariosResponse> Handle(
        ListarUsuariosQuery query, CancellationToken cancellationToken)
    {
        // Listado admin de usuarios cruza empresas: el filtro EmpresaId
        // es explícito en el query, no del query filter global. Sin
        // bypass, el join contra UsuarioEmpresaRoles oculta filas de
        // otras empresas y los filtros por rol/empresa devuelven menos
        // de lo esperado. Endpoint gated por identidad.usuarios.leer.
        using var bypass = _empresaContext.Bypass();

        var offset = query.Offset < 0 ? 0 : query.Offset;
        var limit = query.Limit is <= 0 or > LimitMax
            ? Math.Min(50, LimitMax)
            : query.Limit;

        var q = _db.Usuarios.AsNoTracking();

        if (query.SoloActivos is true)
        {
            q = q.Where(u => u.Activo);
        }

        // Subquery anti-N+1: cuando hay filtro por empresa/rol, joineamos
        // contra UsuarioEmpresaRoles. Mantener AsNoTracking en ambos lados
        // para evitar tracking innecesario.
        if (query.EmpresaId is Guid empresaId)
        {
            var idsConAsignacionEnEmpresa = _db.UsuarioEmpresaRoles.AsNoTracking()
                .Where(uer => uer.EmpresaId == empresaId)
                .Select(uer => uer.UsuarioId);
            q = q.Where(u => idsConAsignacionEnEmpresa.Contains(u.Id));
        }

        if (query.RolId is Guid rolId)
        {
            var idsConRol = _db.UsuarioEmpresaRoles.AsNoTracking()
                .Where(uer => uer.RolId == rolId)
                .Select(uer => uer.UsuarioId);
            q = q.Where(u => idsConRol.Contains(u.Id));
        }

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(u => u.Nombre)
            .Skip(offset).Take(limit)
            .Select(u => new UsuarioResponse(
                u.Id, u.Email, u.EntraOid, u.Nombre,
                u.DepartamentoId, u.Activo, u.Version, null))
            .ToListAsync(cancellationToken);

        // El vínculo vive en compartido.empleados. Resolver únicamente los
        // usuarios de esta página evita consultas por fila y no exige al
        // operador un permiso adicional del catálogo de empleados.
        var usuarioIds = items.Select(u => u.Id).ToArray();
        var vinculos = await _compartido.Empleados.AsNoTracking()
            .Where(e => e.UsuarioId.HasValue && usuarioIds.Contains(e.UsuarioId.Value))
            .Select(e => new { e.UsuarioId, EmpleadoId = e.Id })
            .ToListAsync(cancellationToken);
        // Si QA aún contiene vínculos duplicados, la bandeja debe seguir
        // disponible para diagnosticarlos; el reporte A–G conserva la
        // responsabilidad de señalar/corregir ese dato histórico.
        var empleadoPorUsuario = vinculos
            .GroupBy(e => e.UsuarioId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.EmpleadoId).First().EmpleadoId);
        items = items.Select(u => u with {
            EmpleadoId = empleadoPorUsuario.TryGetValue(u.Id, out var empleadoId) ? empleadoId : null
        }).ToList();

        return new ListarUsuariosResponse(items, total);
    }
}
