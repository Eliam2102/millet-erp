using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Incidencias;

namespace Millet.Compartido.Application.Incidencias;

internal static class FiltroEstados
{
    /// <summary>Texto → enum; sin estados = pendientes (Abierta y EnAtencion) cuando <paramref name="pendientesPorOmision"/>.</summary>
    public static EstadoIncidencia[]? Parse(IReadOnlyCollection<string>? estados, bool pendientesPorOmision)
    {
        if (estados is null || estados.Count == 0)
            return pendientesPorOmision ? [EstadoIncidencia.Abierta, EstadoIncidencia.EnAtencion] : null;
        var r = new List<EstadoIncidencia>();
        foreach (var e in estados)
        {
            if (!Enum.TryParse<EstadoIncidencia>(e, ignoreCase: true, out var v) || !Enum.IsDefined(v))
                throw new BusinessRuleException("INCIDENCIA_ESTADO_INVALIDO", $"Estado no válido: «{e}». Use Abierta, EnAtencion, Resuelta o Cancelada.");
            r.Add(v);
        }
        return [.. r.Distinct()];
    }

    public static (int Offset, int Limit) Pagina(int? offset, int? limit) =>
        (Math.Max(0, offset ?? 0), Math.Clamp(limit ?? 50, 1, 200));
}

/// <summary>«Mis incidencias»: las asignadas al usuario actual (por omisión, pendientes), de la más vieja a la más nueva.</summary>
public sealed record ListarMisIncidenciasQuery(IReadOnlyCollection<string>? Estados, string? Modulo, int? Offset, int? Limit) : IRequest<IncidenciasPagina>;

public sealed class ListarMisIncidenciasHandler(CompartidoDbContext db, ICurrentUserContext usuario) : IRequestHandler<ListarMisIncidenciasQuery, IncidenciasPagina>
{
    public async Task<IncidenciasPagina> Handle(ListarMisIncidenciasQuery r, CancellationToken cancellationToken)
    {
        var u = usuario.UserId ?? throw new ForbiddenException("USUARIO_REQUERIDO", "No se pudo identificar al usuario.");
        var estados = FiltroEstados.Parse(r.Estados, pendientesPorOmision: true)!;
        var (offset, limit) = FiltroEstados.Pagina(r.Offset, r.Limit);
        var q = db.Incidencias.AsNoTracking().Where(x => x.ResponsableUsuarioId == u && estados.Contains(x.Estado));
        if (!string.IsNullOrWhiteSpace(r.Modulo)) q = q.Where(x => x.Modulo == r.Modulo.Trim());
        var total = await q.CountAsync(cancellationToken);
        var items = await q.OrderBy(x => x.AbiertaEn).ThenBy(x => x.Numero).Skip(offset).Take(limit).ToListAsync(cancellationToken);
        return new IncidenciasPagina(items.Select(IncidenciaFila.De).ToList(), total, offset, limit);
    }
}

public sealed record ResumenMisIncidenciasQuery : IRequest<ResumenMisIncidencias>;

public sealed class ResumenMisIncidenciasHandler(CompartidoDbContext db, ICurrentUserContext usuario) : IRequestHandler<ResumenMisIncidenciasQuery, ResumenMisIncidencias>
{
    public async Task<ResumenMisIncidencias> Handle(ResumenMisIncidenciasQuery r, CancellationToken cancellationToken)
    {
        if (usuario.UserId is not Guid u) return new ResumenMisIncidencias(0, 0);
        var conteo = await db.Incidencias.AsNoTracking()
            .Where(x => x.ResponsableUsuarioId == u && (x.Estado == EstadoIncidencia.Abierta || x.Estado == EstadoIncidencia.EnAtencion))
            .GroupBy(x => x.Estado).Select(g => new { g.Key, N = g.Count() }).ToListAsync(cancellationToken);
        return new ResumenMisIncidencias(
            conteo.FirstOrDefault(c => c.Key == EstadoIncidencia.Abierta)?.N ?? 0,
            conteo.FirstOrDefault(c => c.Key == EstadoIncidencia.EnAtencion)?.N ?? 0);
    }
}

/// <summary>Bandeja general y consulta por documento (con <c>DocumentoId</c>), filtrada por alcance de sucursal; más nuevas primero.</summary>
public sealed record ListarIncidenciasQuery(
    string? Modulo,
    string? TipoDocumento,
    Guid? DocumentoId,
    IReadOnlyCollection<string>? Estados,
    Guid? SucursalId,
    Guid? ResponsableId,
    int? Offset,
    int? Limit) : IRequest<IncidenciasPagina>;

public sealed class ListarIncidenciasHandler(CompartidoDbContext db, IncidenciasServicio servicio, ICurrentUserPermissions permisos)
    : IRequestHandler<ListarIncidenciasQuery, IncidenciasPagina>
{
    public async Task<IncidenciasPagina> Handle(ListarIncidenciasQuery r, CancellationToken cancellationToken)
    {
        var u = servicio.UsuarioRequerido;
        if (!await permisos.TieneAsync(IncidenciasPermisos.Ver, cancellationToken))
            throw new ForbiddenException("INCIDENCIA_SIN_PERMISO", "No tienes permiso para consultar incidencias.");
        var estados = FiltroEstados.Parse(r.Estados, pendientesPorOmision: false);
        var (offset, limit) = FiltroEstados.Pagina(r.Offset, r.Limit);
        var visibles = await servicio.SucursalesVisiblesAsync(cancellationToken);

        var q = db.Incidencias.AsNoTracking().AsQueryable();
        if (visibles is not null)
            q = q.Where(x => x.SucursalId == null || visibles.Contains(x.SucursalId.Value) || x.ResponsableUsuarioId == u);
        if (!string.IsNullOrWhiteSpace(r.Modulo)) q = q.Where(x => x.Modulo == r.Modulo.Trim());
        if (!string.IsNullOrWhiteSpace(r.TipoDocumento)) q = q.Where(x => x.TipoDocumento == r.TipoDocumento.Trim());
        if (r.DocumentoId is Guid d) q = q.Where(x => x.DocumentoId == d);
        if (estados is not null) q = q.Where(x => estados.Contains(x.Estado));
        if (r.SucursalId is Guid s) q = q.Where(x => x.SucursalId == s);
        if (r.ResponsableId is Guid resp) q = q.Where(x => x.ResponsableUsuarioId == resp);

        var total = await q.CountAsync(cancellationToken);
        var items = await q.OrderByDescending(x => x.AbiertaEn).ThenByDescending(x => x.Numero).Skip(offset).Take(limit).ToListAsync(cancellationToken);
        return new IncidenciasPagina(items.Select(IncidenciaFila.De).ToList(), total, offset, limit);
    }
}

public sealed record ObtenerIncidenciaQuery(Guid Id) : IRequest<IncidenciaDetalle>;

public sealed class ObtenerIncidenciaHandler(IncidenciasServicio servicio) : IRequestHandler<ObtenerIncidenciaQuery, IncidenciaDetalle>
{
    public async Task<IncidenciaDetalle> Handle(ObtenerIncidenciaQuery r, CancellationToken cancellationToken) =>
        await servicio.DetalleAsync(await servicio.CargarAsync(r.Id, false, cancellationToken), cancellationToken);
}
