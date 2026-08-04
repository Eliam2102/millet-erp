using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.Salidas;

// ============================================================================
// Queries de salidas (F4-PR1). Bandeja paginada + detalle.
// Filtra `movimientos_inventario` por tipo IN (SalidaConsumo, SalidaPorVale).
// ============================================================================

public sealed record SalidaListItem(
    Guid Id,
    string Folio,
    DateOnly FechaMovimiento,
    Guid SubAlmacenId,
    Guid? RqId,
    bool EsPorVale,
    Guid? PersonaDestinatariaId,
    decimal MontoTotalMxn,
    EstadoMovimiento Estado,
    /// <summary>
    /// RQ vinculada posteriormente a un vale para regularizarlo (A14).
    /// Null si el vale aún no se regulariza, o si la salida no es por vale.
    /// </summary>
    Guid? RqRegularizadoraId,
    /// <summary>
    /// Folio legible de la RQ surtida (ADR-0042, batch vía
    /// <c>IComprasRequisicionReadPort</c>). Null si la salida no tiene RQ
    /// directa (vales) o el puerto no resolvió — el frontend muestra "—",
    /// nunca el GUID.
    /// </summary>
    string? RqFolio = null,
    /// <summary>
    /// Folio de la RQ regularizadora de un vale (A14), mismo batch. La
    /// bandeja lo muestra como "Vale · {folio}" para distinguirlo de una
    /// RQ directa. Null si el vale no se ha regularizado.
    /// </summary>
    string? RqRegularizadoraFolio = null);

public sealed record SalidaDetalle(
    Guid Id,
    string Folio,
    DateOnly FechaMovimiento,
    Guid SubAlmacenId,
    // Nombres resueltos en backend (ADR-0042). Null = el read-port no resolvió;
    // el frontend cae al id/clave cruda.
    string? SubAlmacenClave,
    string? SubAlmacenNombre,
    Guid? RqId,
    string? RqFolio,
    bool EsPorVale,
    string? ValeBlobRef,
    Guid? PersonaDestinatariaId,
    string? PersonaDestinatariaNombre,
    EstadoMovimiento Estado,
    int Version,
    string? Observaciones,
    DateTimeOffset? RegistradoAt,
    Guid? RegistradoPor,
    /// <summary>
    /// RQ vinculada posteriormente para regularizar un vale (A14).
    /// Null si el vale aún no se regulariza, o si la salida es Variante A.
    /// </summary>
    Guid? RqRegularizadoraId,
    string? RqRegularizadoraFolio,
    IReadOnlyList<SalidaLineaItem> Lineas);

public sealed record SalidaLineaItem(
    Guid Id,
    int Posicion,
    Guid ArticuloId,
    // Resueltos en backend (ADR-0042). Null = el read-port no resolvió.
    string? ArticuloClave,
    string? ArticuloDescripcion,
    decimal Cantidad,
    string UnidadMedida,
    decimal CostoUnitarioMxn,
    decimal MontoTotalMxn,
    Guid? CentroCostoId,
    // Resueltos en backend vía ICentroCostoReadPort (sin filtro, incluye
    // inactivas — ADR-0050 §3). Null = el read-port no resolvió → el FE cae a
    // "No catalogado".
    string? CentroCostoClave,
    string? CentroCostoNombre,
    Guid? ProyectoId);

public sealed record ListarSalidasQuery(
    EstadoMovimiento? Estado,
    Guid? SubAlmacenId,
    Guid? RqId,
    Guid? PersonaDestinatariaId,
    DateOnly? Desde,
    DateOnly? Hasta,
    bool? SoloVales,
    /// <summary>
    /// Filtro UX (FE-F7+): cuando <c>true</c>, devuelve únicamente
    /// salidas tipo <c>SalidaPorVale</c> que aún NO han sido
    /// regularizadas con una RQ posterior (<c>RqRegularizadoraId IS NULL</c>).
    /// Útil para la bandeja "Vales pendientes de regularizar" (A14, SLA 48h).
    /// </summary>
    bool? NoRegularizados,
    int Offset,
    int Limit) : IRequest<AlmacenPagedResponse<SalidaListItem>>;

public sealed class ListarSalidasHandler
    : IRequestHandler<ListarSalidasQuery, AlmacenPagedResponse<SalidaListItem>>
{
    private readonly AlmacenDbContext _db;
    private readonly IComprasRequisicionReadPort _requisiciones;

    public ListarSalidasHandler(AlmacenDbContext db, IComprasRequisicionReadPort requisiciones)
    {
        _db = db;
        _requisiciones = requisiciones;
    }

    public async Task<AlmacenPagedResponse<SalidaListItem>> Handle(
        ListarSalidasQuery request, CancellationToken cancellationToken)
    {
        IQueryable<MovimientoInventario> query = _db.Movimientos.AsNoTracking()
            .Where(m => m.Tipo == TipoMovimiento.SalidaConsumo
                || m.Tipo == TipoMovimiento.SalidaPorVale);
        if (request.Estado is EstadoMovimiento e) query = query.Where(m => m.Estado == e);
        // PR6a: el sub-almacén ya no vive en la cabecera; se filtra vía la vista.
        if (request.SubAlmacenId is Guid sid)
            query = query.Where(m => _db.MovimientosSubAlmacen
                .Any(v => v.MovimientoId == m.Id && v.SubAlmacenId == sid));
        if (request.RqId is Guid rqid) query = query.Where(m => m.RqId == rqid);
        if (request.PersonaDestinatariaId is Guid pid)
            query = query.Where(m => m.PersonaDestinatariaId == pid);
        if (request.Desde is DateOnly d) query = query.Where(m => m.FechaMovimiento >= d);
        if (request.Hasta is DateOnly h) query = query.Where(m => m.FechaMovimiento <= h);
        if (request.SoloVales is true) query = query.Where(m => m.Tipo == TipoMovimiento.SalidaPorVale);
        if (request.NoRegularizados is true)
        {
            // Implícitamente solo aplica a vales (las salidas con RQ ya
            // están "regularizadas" desde el inicio). Combinamos los dos
            // filtros para garantizar consistencia incluso si el caller
            // no pasa SoloVales=true.
            query = query.Where(m =>
                m.Tipo == TipoMovimiento.SalidaPorVale
                && m.RqRegularizadoraId == null);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(m => m.FechaMovimiento).ThenByDescending(m => m.FechaRegistro)
            .Skip(request.Offset).Take(request.Limit)
            .Select(m => new SalidaListItem(
                m.Id,
                m.Folio ?? "(borrador)",
                m.FechaMovimiento,
                // PR6a: sub-almacén derivado de la línea vía la vista.
                _db.MovimientosSubAlmacen
                    .Where(v => v.MovimientoId == m.Id)
                    .Select(v => v.SubAlmacenId)
                    .FirstOrDefault(),
                m.RqId,
                m.Tipo == TipoMovimiento.SalidaPorVale,
                m.PersonaDestinatariaId,
                m.Lineas.Sum(l => l.MontoTotalMxn),
                m.Estado,
                m.RqRegularizadoraId,
                null,
                null))
            .ToListAsync(cancellationToken);

        // Folios de RQ en batch (ADR-0042): una sola llamada con los ids
        // distintos de RQ directa (SalidaConsumo) Y RQ regularizadora de
        // vale (A14). La columna RQ de la bandeja muestra folio, nunca GUID.
        var rqIds = items
            .SelectMany(i => new[] { i.RqId, i.RqRegularizadoraId })
            .OfType<Guid>()
            .Distinct()
            .ToArray();
        if (rqIds.Length > 0)
        {
            var folios = await _requisiciones.ObtenerFoliosAsync(rqIds, cancellationToken);
            items = items
                .Select(i => i with
                {
                    RqFolio = i.RqId is Guid rqId && folios.TryGetValue(rqId, out var folio)
                        ? folio
                        : null,
                    RqRegularizadoraFolio =
                        i.RqRegularizadoraId is Guid regId && folios.TryGetValue(regId, out var folioReg)
                            ? folioReg
                            : null,
                })
                .ToList();
        }

        return new AlmacenPagedResponse<SalidaListItem>(items, request.Offset, request.Limit, total);
    }
}

public sealed record ObtenerSalidaPorIdQuery(Guid Id) : IRequest<SalidaDetalle?>;

public sealed class ObtenerSalidaPorIdHandler
    : IRequestHandler<ObtenerSalidaPorIdQuery, SalidaDetalle?>
{
    private readonly AlmacenDbContext _db;
    private readonly IArticuloReadPort _articulos;
    private readonly IComprasRequisicionReadPort _requisiciones;
    private readonly IUsuarioReadPort _usuarios;
    private readonly ICentroCostoReadPort _centrosCosto;

    public ObtenerSalidaPorIdHandler(
        AlmacenDbContext db,
        IArticuloReadPort articulos,
        IComprasRequisicionReadPort requisiciones,
        IUsuarioReadPort usuarios,
        ICentroCostoReadPort centrosCosto)
    {
        _db = db;
        _articulos = articulos;
        _requisiciones = requisiciones;
        _usuarios = usuarios;
        _centrosCosto = centrosCosto;
    }

    public async Task<SalidaDetalle?> Handle(
        ObtenerSalidaPorIdQuery request, CancellationToken cancellationToken)
    {
        var mov = await _db.Movimientos.AsNoTracking()
            .Include(m => m.Lineas)
            .FirstOrDefaultAsync(m => m.Id == request.Id
                && (m.Tipo == TipoMovimiento.SalidaConsumo
                    || m.Tipo == TipoMovimiento.SalidaPorVale), cancellationToken);
        if (mov is null) return null;

        // Enriquecimiento de nombres (ADR-0042). Cada resolución cae al id/clave
        // cruda si el read-port no la encuentra (campo null en el DTO).

        // PR6a: sub-almacén derivado de la ubicación de la línea vía la vista.
        var subAlmacenId = await _db.MovimientosSubAlmacen.AsNoTracking()
            .Where(v => v.MovimientoId == mov.Id)
            .Select(v => v.SubAlmacenId)
            .FirstOrDefaultAsync(cancellationToken);

        // Sub-almacén: catálogo intra-módulo, lookup local (1 query).
        var subAlmacen = await _db.SubAlmacenes.AsNoTracking()
            .Where(s => s.Id == subAlmacenId)
            .Select(s => new { s.Clave, s.Nombre })
            .FirstOrDefaultAsync(cancellationToken);

        // Artículo: batch sobre los ids distintos de las líneas (anti-N+1).
        var articuloIds = mov.Lineas.Select(l => l.ArticuloId).Distinct().ToArray();
        var articulos = await _articulos.ObtenerPorIdsAsync(articuloIds, cancellationToken);

        // Folio de RQ y de RQ regularizadora: batch state-agnostic (resuelve
        // aunque la RQ ya esté Surtida/Cerrada).
        var rqIds = new[] { mov.RqId, mov.RqRegularizadoraId }
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var folios = rqIds.Length > 0
            ? await _requisiciones.ObtenerFoliosAsync(rqIds, cancellationToken)
            : new Dictionary<Guid, string>();

        // Persona destinataria: usuario de Identidad.
        string? personaNombre = null;
        if (mov.PersonaDestinatariaId is Guid personaId)
        {
            var nombres = await _usuarios.ObtenerNombresAsync(new[] { personaId }, cancellationToken);
            nombres.TryGetValue(personaId, out personaNombre);
        }

        // Líneas base (artículo resuelto; CC-Máquina clave/nombre = null aún).
        var lineas = mov.Lineas
            .OrderBy(l => l.Posicion)
            .Select(l =>
            {
                articulos.TryGetValue(l.ArticuloId, out var art);
                return new SalidaLineaItem(
                    l.Id, l.Posicion, l.ArticuloId,
                    art?.Clave, art?.Descripcion,
                    l.Cantidad, l.UnidadMedida,
                    l.CostoUnitarioMxn, l.MontoTotalMxn,
                    l.CentroCostoId, CentroCostoClave: null, CentroCostoNombre: null,
                    l.ProyectoId);
            })
            .ToList();

        // CC-Máquina (Fase E PR5): batch sobre los ids distintos (anti-N+1), sin
        // filtro de alcance e incluyendo inactivas (ADR-0050 §3). Resuelve
        // clave/nombre; los irresolubles quedan null → "No catalogado" en el FE.
        var centroCostoIds = ExtraerCentroCostoIdsDistintos(lineas);
        var centrosCosto = centroCostoIds.Count > 0
            ? await _centrosCosto.ObtenerAsync(centroCostoIds, cancellationToken)
            : new Dictionary<Guid, Dim3Lectura>();
        lineas = AplicarCentrosCosto(lineas, centrosCosto);

        return new SalidaDetalle(
            Id: mov.Id,
            Folio: mov.Folio ?? "(borrador)",
            FechaMovimiento: mov.FechaMovimiento,
            SubAlmacenId: subAlmacenId,
            SubAlmacenClave: subAlmacen?.Clave,
            SubAlmacenNombre: subAlmacen?.Nombre,
            RqId: mov.RqId,
            RqFolio: mov.RqId is Guid rqId && folios.TryGetValue(rqId, out var f) ? f : null,
            EsPorVale: mov.Tipo == TipoMovimiento.SalidaPorVale,
            ValeBlobRef: mov.ValeBlobRef,
            PersonaDestinatariaId: mov.PersonaDestinatariaId,
            PersonaDestinatariaNombre: personaNombre,
            Estado: mov.Estado,
            Version: mov.Version,
            Observaciones: mov.ComentarioLibre,
            RegistradoAt: mov.RegistradoAt,
            RegistradoPor: mov.RegistradoPor,
            RqRegularizadoraId: mov.RqRegularizadoraId,
            RqRegularizadoraFolio: mov.RqRegularizadoraId is Guid rrId
                && folios.TryGetValue(rrId, out var rf) ? rf : null,
            Lineas: lineas);
    }

    /// <summary>
    /// Fase E PR5: ids de CC-Máquina distintos de las líneas (omite null) para
    /// una sola query batch al read-port (anti-N+1). Molde PR3/PR4.
    /// </summary>
    public static IReadOnlyList<Guid> ExtraerCentroCostoIdsDistintos(
        IReadOnlyList<SalidaLineaItem> lineas) =>
        lineas.Where(l => l.CentroCostoId is not null)
            .Select(l => l.CentroCostoId!.Value)
            .Distinct()
            .ToList();

    /// <summary>
    /// Fase E PR5: puebla CentroCostoClave/Nombre por línea desde el diccionario
    /// resuelto. Incluye inactivas (resuelven igual, ADR-0049); un id que no
    /// aparece queda con clave/nombre null → "No catalogado" en el FE.
    /// </summary>
    public static List<SalidaLineaItem> AplicarCentrosCosto(
        IReadOnlyList<SalidaLineaItem> lineas,
        IReadOnlyDictionary<Guid, Dim3Lectura> centrosCosto) =>
        lineas.Select(l => l.CentroCostoId is Guid cc && centrosCosto.TryGetValue(cc, out var d)
            ? l with { CentroCostoClave = d.Clave, CentroCostoNombre = d.Nombre }
            : l).ToList();
}
