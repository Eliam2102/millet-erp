using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Incidencias;
using Millet.SharedKernel.Application.Tramos;
using Millet.SharedKernel.Domain.Tramos;

namespace Millet.Compartido.Application.Tramos;

public sealed record TramoOperativoDto(string Clave, string Nombre, string Modulo, bool Detenido, string? Motivo, Guid? DetenidoPorId, DateTimeOffset? DetenidoAt,
    Guid? IncidenciaId, DateTimeOffset? UltimoEventoCorrectoAt, string? UltimoEventoCorrecto, Guid? ReanudadoPorId, DateTimeOffset? ReanudadoAt, string? MotivoReanudacion);

/// <summary>M-303: estado de todos los tramos del catálogo (los que nunca se tocaron salen en marcha). Lo ve cualquier usuario: la captura muestra el aviso.</summary>
public sealed record ListarTramosQuery : IRequest<IReadOnlyList<TramoOperativoDto>>;

/// <summary>M-303: detiene el tramo sin apagar la aplicación y abre la incidencia con el último evento correcto.</summary>
public sealed record DetenerTramoCommand(string Clave, string Motivo) : IRequest<TramoOperativoDto>;

/// <summary>M-303: reanuda el tramo; lo autoriza otra persona distinta de quien lo detuvo. Resuelve la incidencia.</summary>
public sealed record ReanudarTramoCommand(string Clave, string Motivo) : IRequest<TramoOperativoDto>;

internal static class TramosMapeo
{
    public static TramoOperativoDto Dto(TramoOperativoInfo i, TramoOperativo? t) => new(i.Clave, i.Nombre, i.Modulo, t?.Detenido ?? false, t?.Motivo, t?.DetenidoPorId, t?.DetenidoAt,
        t?.IncidenciaId, t?.UltimoEventoCorrectoAt, t?.UltimoEventoCorrecto, t?.ReanudadoPorId, t?.ReanudadoAt, t?.MotivoReanudacion);

    public static TramoOperativoInfo Info(string clave) => TramosOperativos.Buscar(clave?.Trim() ?? "")
        ?? throw new EntityNotFoundException("TRAMO_NO_EXISTE", $"No existe el tramo {clave}.");
}

public sealed class ListarTramosHandler(CompartidoDbContext db) : IRequestHandler<ListarTramosQuery, IReadOnlyList<TramoOperativoDto>>
{
    public async Task<IReadOnlyList<TramoOperativoDto>> Handle(ListarTramosQuery q, CancellationToken cancellationToken)
    {
        var filas = await db.TramosOperativos.AsNoTracking().ToDictionaryAsync(t => t.Clave, cancellationToken);
        return [.. TramosOperativos.Todos.Select(i => TramosMapeo.Dto(i, filas.GetValueOrDefault(i.Clave)))];
    }
}

public sealed class DetenerTramoHandler(CompartidoDbContext db, ICurrentUserContext user, ICurrentUserPermissions permisos, IIncidenciasPort incidencias, IClock clock)
    : IRequestHandler<DetenerTramoCommand, TramoOperativoDto>
{
    public async Task<TramoOperativoDto> Handle(DetenerTramoCommand c, CancellationToken cancellationToken)
    {
        if (!await permisos.TieneAsync(TramosOperativos.PermisoGestionar, cancellationToken))
            throw new ForbiddenException("TRAMO_PERMISO_REQUERIDO", "No tiene permiso para detener tramos.");
        var actor = user.UserId ?? throw new UnauthorizedAccessException();
        var info = TramosMapeo.Info(c.Clave);
        var tramo = await db.TramosOperativos.FirstOrDefaultAsync(t => t.Clave == info.Clave, cancellationToken);
        if (tramo is null) db.TramosOperativos.Add(tramo = new TramoOperativo(info.Clave));
        tramo.Detener(c.Motivo, actor, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        var ultimo = tramo.UltimoEventoCorrectoAt is { } at ? $"{at:yyyy-MM-dd HH:mm} UTC · {tramo.UltimoEventoCorrecto}" : "sin evento correcto registrado";
        var inc = await incidencias.AbrirAsync(new AbrirIncidenciaSolicitud(info.Modulo, "tramo_operativo", tramo.Id, info.Nombre, TramosOperativos.PasoIncidencia,
            tramo.Motivo!, actor, Detalle: $"Último evento correcto: {ultimo}", DetalleClave: tramo.DetenidoAt!.Value.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)), cancellationToken);
        tramo.LigarIncidencia(inc.Id);
        await db.SaveChangesAsync(cancellationToken);
        return TramosMapeo.Dto(info, tramo);
    }
}

public sealed class ReanudarTramoHandler(CompartidoDbContext db, ICurrentUserContext user, ICurrentUserPermissions permisos, IIncidenciasPort incidencias, IClock clock)
    : IRequestHandler<ReanudarTramoCommand, TramoOperativoDto>
{
    public async Task<TramoOperativoDto> Handle(ReanudarTramoCommand c, CancellationToken cancellationToken)
    {
        if (!await permisos.TieneAsync(TramosOperativos.PermisoGestionar, cancellationToken))
            throw new ForbiddenException("TRAMO_PERMISO_REQUERIDO", "No tiene permiso para reanudar tramos.");
        var actor = user.UserId ?? throw new UnauthorizedAccessException();
        var info = TramosMapeo.Info(c.Clave);
        var tramo = await db.TramosOperativos.FirstOrDefaultAsync(t => t.Clave == info.Clave, cancellationToken)
            ?? throw new BusinessRuleException("TRAMO_NO_DETENIDO", "El tramo no está detenido.");
        tramo.Reanudar(c.Motivo, actor, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        await incidencias.ResolverPorDocumentoAsync(TramosOperativos.PasoIncidencia, tramo.Id, null, $"Tramo reanudado: {tramo.MotivoReanudacion}", cancellationToken);
        return TramosMapeo.Dto(info, tramo);
    }
}
