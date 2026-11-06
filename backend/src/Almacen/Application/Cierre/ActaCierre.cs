using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Cierre;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Notificaciones;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Cierre;

/// <summary>Contenido de la foto del acta (86e3tac2y): totales por almacén, conteos aplicados y vales atendidos del mes.</summary>
public sealed record ActaContenido(
    IReadOnlyList<ActaAlmacenTotal> Almacenes,
    IReadOnlyList<ActaConteo> Conteos,
    IReadOnlyList<ActaVale> Vales);

public sealed record ActaAlmacenTotal(string Almacen, int Renglones, decimal ValorInventarioMxn);
public sealed record ActaConteo(string Folio, string Tipo, DateTimeOffset? FechaAprobacion);
public sealed record ActaVale(string Folio, DateOnly Fecha);

public sealed record ActaCierreResponse(
    Guid Id, int Anio, int Mes, string Estado,
    Guid EjecutadoPorId, string EjecutadoPor, DateTimeOffset EjecutadoAt,
    Guid? RevisadaPorId, string? RevisadaPor, DateTimeOffset? RevisadaAt,
    ActaContenido Contenido);

public static class ActaCierre
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web);

    /// <summary>Arma la foto del acta. Llamar antes del <c>SaveChanges</c> del cierre: las valuaciones aún están en el tracker.</summary>
    public static async Task<ActaCierreMes> CrearAsync(AlmacenDbContext db, PeriodoCerrado periodo, CancellationToken ct)
    {
        var inicio = new DateOnly(periodo.Anio, periodo.Mes, 1);
        var fin = inicio.AddMonths(1).AddDays(-1);
        var inicioUtc = new DateTimeOffset(inicio.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var finUtc = inicioUtc.AddMonths(1);

        var porSub = db.ValuacionesCierre.Local.Where(v => v.PeriodoCerradoId == periodo.Id)
            .GroupBy(v => v.SubAlmacenId).ToDictionary(g => g.Key, g => (Renglones: g.Count(), Valor: g.Sum(v => v.ValorMxn)));
        var subIds = porSub.Keys.ToArray();
        var nombres = await db.SubAlmacenes.AsNoTracking().Where(s => subIds.Contains(s.Id))
            .Join(db.Almacenes, s => s.AlmacenId, a => a.Id, (s, a) => new { s.Id, a.Nombre }).ToListAsync(ct);
        var almacenes = nombres.GroupBy(n => n.Nombre).OrderBy(g => g.Key)
            .Select(g => new ActaAlmacenTotal(g.Key, g.Sum(n => porSub[n.Id].Renglones), g.Sum(n => porSub[n.Id].Valor))).ToList();

        var conteos = (await db.Set<ConteoInventario>().AsNoTracking()
            .Where(c => c.Estado == EstadoConteo.Aplicado && c.FechaAprobacion >= inicioUtc && c.FechaAprobacion < finUtc)
            .OrderBy(c => c.FechaAprobacion).Select(c => new { c.Id, c.Tipo, c.FechaAprobacion }).ToListAsync(ct))
            .Select(c => new ActaConteo(AvisosInternosNotificacionService.FolioConteo(c.Id), c.Tipo.ToString(), c.FechaAprobacion)).ToList();

        var vales = (await db.Movimientos.AsNoTracking()
            .Where(m => m.Tipo == TipoMovimiento.SalidaPorVale && !m.PendienteRegularizacion && m.RqRegularizadoraId != null
                && m.FechaMovimiento >= inicio && m.FechaMovimiento <= fin)
            .OrderBy(m => m.FechaMovimiento).Select(m => new { m.Folio, m.FechaMovimiento }).ToListAsync(ct))
            .Select(m => new ActaVale(m.Folio ?? "(s/folio)", m.FechaMovimiento)).ToList();

        return new ActaCierreMes(periodo, JsonSerializer.Serialize(new ActaContenido(almacenes, conteos, vales), Json));
    }

    public static async Task<ActaCierreResponse> ProyectarAsync(ActaCierreMes a, IUsuarioReadPort usuarios, CancellationToken ct)
    {
        var ids = new[] { a.EjecutadoPor, a.RevisadaPor ?? Guid.Empty }.Where(i => i != Guid.Empty).Distinct().ToArray();
        var n = await usuarios.ObtenerNombresAsync(ids, ct);
        var contenido = JsonSerializer.Deserialize<ActaContenido>(a.ContenidoJson, Json) ?? new ActaContenido([], [], []);
        return new ActaCierreResponse(a.Id, a.Anio, a.Mes, a.Estado, a.EjecutadoPor, n.GetValueOrDefault(a.EjecutadoPor, "—"), a.EjecutadoAt,
            a.RevisadaPor, a.RevisadaPor is { } r ? n.GetValueOrDefault(r, "—") : null, a.RevisadaAt, contenido);
    }
}

/// <summary>Actas de los últimos 12 periodos cerrados, la más reciente primero.</summary>
public sealed record ListarActasCierreQuery : IRequest<IReadOnlyList<ActaCierreResponse>>;

public sealed class ListarActasCierreHandler(AlmacenDbContext db, IUsuarioReadPort usuarios)
    : IRequestHandler<ListarActasCierreQuery, IReadOnlyList<ActaCierreResponse>>
{
    public async Task<IReadOnlyList<ActaCierreResponse>> Handle(ListarActasCierreQuery request, CancellationToken cancellationToken)
    {
        var actas = await db.ActasCierre.AsNoTracking().OrderByDescending(a => a.Anio).ThenByDescending(a => a.Mes).Take(12).ToListAsync(cancellationToken);
        var respuestas = new List<ActaCierreResponse>();
        foreach (var a in actas) respuestas.Add(await ActaCierre.ProyectarAsync(a, usuarios, cancellationToken));
        return respuestas;
    }
}

/// <summary>La Dirección de Administración marca el acta como revisada; el permiso lo exige el endpoint.</summary>
public sealed record RevisarActaCierreCommand(Guid ActaId) : IRequest<ActaCierreResponse>;

public sealed class RevisarActaCierreHandler(AlmacenDbContext db, ICurrentUserContext currentUser, IUsuarioReadPort usuarios)
    : IRequestHandler<RevisarActaCierreCommand, ActaCierreResponse>
{
    public async Task<ActaCierreResponse> Handle(RevisarActaCierreCommand request, CancellationToken cancellationToken)
    {
        var usuarioId = currentUser.UserId ?? throw new BusinessRuleException("ACTA_SIN_USUARIO", "Se requiere usuario autenticado.");
        var acta = await db.ActasCierre.FirstOrDefaultAsync(a => a.Id == request.ActaId, cancellationToken)
            ?? throw new EntityNotFoundException("ACTA_NO_ENCONTRADA", $"No existe el acta '{request.ActaId}'.");
        acta.MarcarRevisada(usuarioId);
        await db.SaveChangesAsync(cancellationToken);
        return await ActaCierre.ProyectarAsync(acta, usuarios, cancellationToken);
    }
}
