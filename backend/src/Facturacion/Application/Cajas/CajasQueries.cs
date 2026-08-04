using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cajas;

// ---- Listado (bandeja de administración + selector de apertura) ----

/// <summary>
/// Bandeja de cajas (CAJAS-PR1). Desde CAJAS-PR6 también alimenta el
/// selector de apertura del panel "Mi caja": el permiso pasa de
/// <c>administrar</c> a <c>administrar ∨ operar ∨ supervisar</c> (OR
/// resuelto en el handler — el pipeline de authorization solo compone AND);
/// el listado expone solo nombre/estatus/counts, sin datos sensibles.
/// </summary>
public sealed record ListarCajasQuery(bool SoloActivas = false) : IRequest<IReadOnlyList<CajaListadoItem>>;

public sealed record CajaListadoItem(
    Guid Id,
    string Nombre,
    string? Descripcion,
    string Estatus,
    int Sucursales,
    int Canales,
    int Usuarios);

/// <summary>
/// OR de permisos de lectura de caja (<c>administrar ∨ operar ∨ supervisar</c>),
/// resuelto en el handler porque el pipeline de authorization solo compone AND.
/// </summary>
internal static class PermisoLecturaCaja
{
    private const string Administrar = "facturacion.caja.administrar";
    private const string Operar = "facturacion.caja.operar";
    private const string Supervisar = "facturacion.caja.supervisar";

    public static async Task ExigirAsync(
        Millet.SharedKernel.Application.ICurrentUserPermissions permisos,
        CancellationToken cancellationToken)
    {
        if (!await permisos.TieneAsync(Administrar, cancellationToken)
            && !await permisos.TieneAsync(Operar, cancellationToken)
            && !await permisos.TieneAsync(Supervisar, cancellationToken))
        {
            throw new ForbiddenException(
                "CAJA_PERMISO_INSUFICIENTE",
                "La lectura de cajas requiere facturacion.caja.administrar, operar o supervisar.");
        }
    }
}

public sealed class ListarCajasHandler : IRequestHandler<ListarCajasQuery, IReadOnlyList<CajaListadoItem>>
{
    private readonly FacturacionDbContext _db;
    private readonly Millet.SharedKernel.Application.ICurrentUserPermissions _permisos;

    public ListarCajasHandler(
        FacturacionDbContext db,
        Millet.SharedKernel.Application.ICurrentUserPermissions permisos)
    {
        _db = db;
        _permisos = permisos;
    }

    public async Task<IReadOnlyList<CajaListadoItem>> Handle(
        ListarCajasQuery query, CancellationToken cancellationToken)
    {
        await PermisoLecturaCaja.ExigirAsync(_permisos, cancellationToken);

        var q = _db.Cajas.AsNoTracking();
        if (query.SoloActivas)
            q = q.Where(c => c.Estatus == EstatusCatalogo.Activo);

        return await q
            .OrderBy(c => c.Nombre)
            .Select(c => new CajaListadoItem(
                c.Id,
                c.Nombre,
                c.Descripcion,
                c.Estatus.ToString(),
                c.Sucursales.Count,
                c.Canales.Count,
                c.Usuarios.Count))
            .ToListAsync(cancellationToken);
    }
}

// ---- Detalle (master-detail + ETag) ----

/// <summary>Detalle de una caja con sus tres alcances y la versión para el ETag.</summary>
public sealed record CajaDetalleQuery(Guid Id) : IRequest<CajaDetalleResponse>;

public sealed record CajaSucursalItem(Guid SucursalId);
public sealed record CajaCanalItem(short CanalVentaId, string? Nombre);
public sealed record CajaUsuarioItem(Guid UsuarioId);

public sealed record CajaDetalleResponse(
    Guid Id,
    string Nombre,
    string? Descripcion,
    string Estatus,
    IReadOnlyList<CajaSucursalItem> Sucursales,
    IReadOnlyList<CajaCanalItem> Canales,
    IReadOnlyList<CajaUsuarioItem> Usuarios,
    int Version);

public sealed class CajaDetalleHandler : IRequestHandler<CajaDetalleQuery, CajaDetalleResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICanalesVentaReadPort _canales;

    public CajaDetalleHandler(FacturacionDbContext db, ICanalesVentaReadPort canales)
    {
        _db = db;
        _canales = canales;
    }

    public async Task<CajaDetalleResponse> Handle(CajaDetalleQuery query, CancellationToken cancellationToken)
    {
        var caja = await _db.Cajas.AsNoTracking()
            .Include(c => c.Sucursales)
            .Include(c => c.Canales)
            .Include(c => c.Usuarios)
            .FirstOrDefaultAsync(c => c.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CAJA_NO_ENCONTRADA", $"No existe la caja '{query.Id}'.");

        var canales = new List<CajaCanalItem>(caja.Canales.Count);
        foreach (var canal in caja.Canales.OrderBy(c => c.CanalVentaId))
        {
            // Nombre sin filtrar por estatus: un canal desactivado después de
            // asignarse sigue mostrando su nombre en el detalle.
            var nombre = await _canales.ObtenerNombreAsync(canal.CanalVentaId, cancellationToken);
            canales.Add(new CajaCanalItem(canal.CanalVentaId, nombre));
        }

        return new CajaDetalleResponse(
            caja.Id,
            caja.Nombre,
            caja.Descripcion,
            caja.Estatus.ToString(),
            caja.Sucursales.OrderBy(s => s.SucursalId).Select(s => new CajaSucursalItem(s.SucursalId)).ToList(),
            canales,
            caja.Usuarios.OrderBy(u => u.UsuarioId).Select(u => new CajaUsuarioItem(u.UsuarioId)).ToList(),
            caja.Version);
    }
}

// ---- Ajustes pendientes de una caja ([Decisión 12-C], CAJAS-PR7) ----

public sealed record CajaAjusteItem(
    Guid Id,
    Guid CobroMostradorId,
    string? ComprobanteFolio,
    decimal Importe,
    string FormaPago,
    string Motivo,
    DateTimeOffset CreadoEn,
    Guid? AplicadoEnSesionId);

/// <summary>
/// Ajustes de una caja: pendientes (se aplicarán como <c>AjusteCorreccion</c>
/// en la próxima apertura) y, opcionalmente, los ya drenados — para auditar
/// el destino de las cancelaciones sin sesión (`[Decisión 12-C]`).
/// </summary>
public sealed record ListarAjustesCajaQuery(Guid CajaId, bool IncluirAplicados = false)
    : IRequest<IReadOnlyList<CajaAjusteItem>>;

public sealed class ListarAjustesCajaHandler
    : IRequestHandler<ListarAjustesCajaQuery, IReadOnlyList<CajaAjusteItem>>
{
    private readonly FacturacionDbContext _db;
    private readonly Millet.SharedKernel.Application.ICurrentUserPermissions _permisos;

    public ListarAjustesCajaHandler(
        FacturacionDbContext db,
        Millet.SharedKernel.Application.ICurrentUserPermissions permisos)
    {
        _db = db;
        _permisos = permisos;
    }

    public async Task<IReadOnlyList<CajaAjusteItem>> Handle(
        ListarAjustesCajaQuery query, CancellationToken cancellationToken)
    {
        await PermisoLecturaCaja.ExigirAsync(_permisos, cancellationToken);

        var q = _db.CajaAjustesPendientes.AsNoTracking()
            .Where(a => a.CajaId == query.CajaId);
        if (!query.IncluirAplicados)
            q = q.Where(a => a.AplicadoEnSesionId == null);

        var ajustes = await q
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
        if (ajustes.Count == 0)
            return [];

        // Folio del comprobante del cobro original (dos brincos: ajuste →
        // cobro → comprobante), para que el listado sea legible sin UUIDs.
        var cobroIds = ajustes.Select(a => a.CobroMostradorId).Distinct().ToList();
        var folios = await _db.CobrosMostrador.AsNoTracking()
            .Where(c => cobroIds.Contains(c.Id))
            .Join(
                _db.Comprobantes.AsNoTracking(),
                cobro => cobro.ComprobanteId,
                comprobante => comprobante.Id,
                (cobro, comprobante) => new { cobro.Id, comprobante.Folio })
            .ToDictionaryAsync(x => x.Id, x => x.Folio, cancellationToken);

        return ajustes.Select(a => new CajaAjusteItem(
            a.Id, a.CobroMostradorId, folios.GetValueOrDefault(a.CobroMostradorId),
            a.Importe, a.FormaPago, a.Motivo, a.CreatedAt, a.AplicadoEnSesionId)).ToList();
    }
}

// ---- Alcances administrativos ----

/// <summary>Concesiones de alcance de un usuario (o de todos si UsuarioId es null).</summary>
public sealed record ListarUsuarioAlcancesQuery(Guid? UsuarioId) : IRequest<IReadOnlyList<UsuarioAlcanceItem>>;

public sealed record UsuarioAlcanceItem(Guid UsuarioId, Guid? SucursalId, short? CanalVentaId);

public sealed class ListarUsuarioAlcancesHandler
    : IRequestHandler<ListarUsuarioAlcancesQuery, IReadOnlyList<UsuarioAlcanceItem>>
{
    private readonly FacturacionDbContext _db;

    public ListarUsuarioAlcancesHandler(FacturacionDbContext db) => _db = db;

    public async Task<IReadOnlyList<UsuarioAlcanceItem>> Handle(
        ListarUsuarioAlcancesQuery query, CancellationToken cancellationToken)
    {
        var q = _db.UsuariosAlcance.AsNoTracking();
        if (query.UsuarioId is Guid usuarioId)
            q = q.Where(a => a.UsuarioId == usuarioId);

        return await q
            .OrderBy(a => a.UsuarioId).ThenBy(a => a.SucursalId).ThenBy(a => a.CanalVentaId)
            .Select(a => new UsuarioAlcanceItem(a.UsuarioId, a.SucursalId, a.CanalVentaId))
            .ToListAsync(cancellationToken);
    }
}
