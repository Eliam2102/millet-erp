using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cajas.Sesiones;

// ---- DTOs ----

public sealed record CajaSesionListadoItem(
    Guid Id, Guid CajaId, Guid SucursalId, Guid ResponsableUsuarioId, string Estado,
    DateOnly DiaOperacion, DateTimeOffset FechaApertura, DateTimeOffset? FechaCierre,
    decimal FondoApertura, decimal? Diferencia, bool CierreExtemporaneo);

public sealed record CajaSesionCorteDto(string FormaPago, decimal MontoSistema, decimal? MontoDeclarado);

public sealed record CajaMovimientoDto(
    Guid Id, string Tipo, string FormaPago, decimal Importe, string Moneda,
    string Descripcion, string? Referencia, Guid? CobroMostradorId, Guid UsuarioId, DateTimeOffset CreatedAt);

public sealed record CajaSesionDetalleResponse(
    Guid Id, Guid CajaId, string CajaNombre, Guid SucursalId, Guid ResponsableUsuarioId, string Estado,
    DateOnly DiaOperacion, DateTimeOffset FechaApertura, DateTimeOffset? FechaCierre,
    decimal FondoApertura, decimal? EfectivoTeorico, decimal? EfectivoDeclarado, decimal? Diferencia,
    bool CierreExtemporaneo, string? NotasCierre, Guid? AutorizacionAperturaId, int Version,
    IReadOnlyList<CajaSesionCorteDto> Cortes,
    IReadOnlyList<CajaMovimientoDto> Movimientos,
    IReadOnlyList<CajaSesionCorteDto> TotalesPorForma);

/// <summary>
/// Sesión vigente (Abierta/EnArqueo) del usuario actual como responsable.
/// <c>Sesion</c> null = sin sesión. <c>DiaAnteriorPendiente</c> = la sesión
/// es de un día local anterior → la operación de cobro está bloqueada hasta
/// el cierre extemporáneo (§5.2).
/// </summary>
public sealed record SesionActualResponse(CajaSesionDetalleResponse? Sesion, bool DiaAnteriorPendiente);

// ---- Sesión actual del cajero (GET /cajas/sesion-actual, permiso operar) ----

public sealed record SesionActualQuery() : IRequest<SesionActualResponse>;

public sealed class SesionActualHandler : IRequestHandler<SesionActualQuery, SesionActualResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;
    private readonly Domain.Ports.ISucursalesReadPort _sucursales;

    public SesionActualHandler(
        FacturacionDbContext db,
        ICurrentUserContext user,
        IClock clock,
        Domain.Ports.ISucursalesReadPort sucursales)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _sucursales = sucursales;
    }

    public async Task<SesionActualResponse> Handle(SesionActualQuery query, CancellationToken cancellationToken)
    {
        if (_user.UserId is not Guid usuarioId)
            return new SesionActualResponse(null, false);

        var sesion = await _db.CajaSesiones.AsNoTracking()
            .Include(s => s.Cortes)
            .Where(s => s.ResponsableUsuarioId == usuarioId && s.Estado != EstadoCajaSesion.Cerrada)
            .OrderBy(s => s.FechaApertura)
            .FirstOrDefaultAsync(cancellationToken);
        if (sesion is null)
            return new SesionActualResponse(null, false);

        var detalle = await CajaSesionProyector.ProyectarAsync(_db, sesion, cancellationToken);

        var zona = await _sucursales.ObtenerZonaHorariaAsync(sesion.SucursalId, cancellationToken);
        var diaAnterior = DiaOperacion.HoyLocal(_clock.UtcNow, zona) > sesion.DiaOperacion;

        return new SesionActualResponse(detalle, diaAnterior);
    }
}

// ---- Sesiones de una caja (GET /cajas/{id}/sesiones, supervisar ∨ administrar) ----

public sealed record ListarCajaSesionesQuery(Guid CajaId, int Offset, int Limit)
    : IRequest<IReadOnlyList<CajaSesionListadoItem>>;

public sealed class ListarCajaSesionesHandler
    : IRequestHandler<ListarCajaSesionesQuery, IReadOnlyList<CajaSesionListadoItem>>
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentUserPermissions _permisos;

    public ListarCajaSesionesHandler(FacturacionDbContext db, ICurrentUserPermissions permisos)
    {
        _db = db;
        _permisos = permisos;
    }

    public async Task<IReadOnlyList<CajaSesionListadoItem>> Handle(
        ListarCajaSesionesQuery query, CancellationToken cancellationToken)
    {
        await PermisoSupervisarOAdministrar.ExigirAsync(_permisos, cancellationToken);

        var limit = Math.Clamp(query.Limit <= 0 ? 50 : query.Limit, 1, 200);
        var rows = await _db.CajaSesiones.AsNoTracking()
            .Where(s => s.CajaId == query.CajaId)
            .OrderByDescending(s => s.FechaApertura)
            .Skip(Math.Max(0, query.Offset)).Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(s => new CajaSesionListadoItem(
            s.Id, s.CajaId, s.SucursalId, s.ResponsableUsuarioId, s.Estado.ToString(),
            s.DiaOperacion, s.FechaApertura, s.FechaCierre, s.FondoApertura,
            s.Diferencia, s.CierreExtemporaneo)).ToList();
    }
}

// ---- Detalle de una sesión (GET /cajas/sesiones/{id}, supervisar ∨ administrar) ----

public sealed record CajaSesionDetalleQuery(Guid Id) : IRequest<CajaSesionDetalleResponse>;

public sealed class CajaSesionDetalleHandler
    : IRequestHandler<CajaSesionDetalleQuery, CajaSesionDetalleResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentUserPermissions _permisos;

    public CajaSesionDetalleHandler(FacturacionDbContext db, ICurrentUserPermissions permisos)
    {
        _db = db;
        _permisos = permisos;
    }

    public async Task<CajaSesionDetalleResponse> Handle(
        CajaSesionDetalleQuery query, CancellationToken cancellationToken)
    {
        await PermisoSupervisarOAdministrar.ExigirAsync(_permisos, cancellationToken);

        var sesion = await _db.CajaSesiones.AsNoTracking()
            .Include(s => s.Cortes)
            .FirstOrDefaultAsync(s => s.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException("SESION_NO_ENCONTRADA", $"No existe la sesión '{query.Id}'.");

        return await CajaSesionProyector.ProyectarAsync(_db, sesion, cancellationToken);
    }
}

// ---- Helpers ----

/// <summary>
/// Doble permiso del doc §10 (<c>supervisar ∨ administrar</c>): el pipeline de
/// authorization solo compone AND, así que el OR se resuelve en el handler
/// vía <see cref="ICurrentUserPermissions"/>.
/// </summary>
internal static class PermisoSupervisarOAdministrar
{
    private const string Supervisar = "facturacion.caja.supervisar";
    private const string Administrar = "facturacion.caja.administrar";

    public static async Task ExigirAsync(ICurrentUserPermissions permisos, CancellationToken cancellationToken)
    {
        if (await permisos.TieneAsync(Supervisar, cancellationToken)
            || await permisos.TieneAsync(Administrar, cancellationToken))
            return;

        throw new ForbiddenException(
            "SESION_PERMISO_INSUFICIENTE",
            "Consultar sesiones requiere facturacion.caja.supervisar o facturacion.caja.administrar.");
    }
}

internal static class CajaSesionProyector
{
    public static async Task<CajaSesionDetalleResponse> ProyectarAsync(
        FacturacionDbContext db, CajaSesion sesion, CancellationToken cancellationToken)
    {
        var cajaNombre = await db.Cajas.AsNoTracking()
            .Where(c => c.Id == sesion.CajaId)
            .Select(c => c.Nombre)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var movimientos = await db.CajaMovimientos.AsNoTracking()
            .Where(m => m.CajaSesionId == sesion.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        // Totales vivos por forma (mientras está Abierta los cortes aún no
        // existen; el panel "Mi caja" los muestra en tiempo real).
        var totales = movimientos
            .GroupBy(m => m.FormaPago)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new CajaSesionCorteDto(g.Key, g.Sum(m => m.Importe), null))
            .ToList();

        return new CajaSesionDetalleResponse(
            sesion.Id, sesion.CajaId, cajaNombre, sesion.SucursalId, sesion.ResponsableUsuarioId,
            sesion.Estado.ToString(), sesion.DiaOperacion, sesion.FechaApertura, sesion.FechaCierre,
            sesion.FondoApertura, sesion.EfectivoTeorico, sesion.EfectivoDeclarado, sesion.Diferencia,
            sesion.CierreExtemporaneo, sesion.NotasCierre, sesion.AutorizacionAperturaId, sesion.Version,
            sesion.Cortes.OrderBy(c => c.FormaPago, StringComparer.Ordinal)
                .Select(c => new CajaSesionCorteDto(c.FormaPago, c.MontoSistema, c.MontoDeclarado)).ToList(),
            movimientos.Select(m => new CajaMovimientoDto(
                m.Id, m.Tipo.ToString(), m.FormaPago, m.Importe, m.Moneda, m.Descripcion,
                m.Referencia, m.CobroMostradorId, m.UsuarioId, m.CreatedAt)).ToList(),
            totales);
    }
}
