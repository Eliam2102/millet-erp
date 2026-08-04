using MapsterMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.ObtenerOrdenCompraPorId;

/// <summary>
/// Handler de <see cref="ObtenerOrdenCompraPorIdQuery"/>. Lookup directo
/// por <c>Id</c>; el global query filter de <c>BaseDbContext</c> aplica
/// la restricción de empresa actual (ADR-0011) y soft delete (ADR-0008)
/// automáticamente.
///
/// Lanza <see cref="EntityNotFoundException"/> con código
/// <c>ORDEN_COMPRA_NO_ENCONTRADA</c> si el id no existe o no es accesible
/// para la empresa actual; <c>GlobalExceptionHandler</c> lo traduce a 404
/// con ProblemDetails (ADR-0010).
///
/// <para>
/// Enriquece cada línea con: (1) el <c>RequisicionFolio</c> humano de su RQ
/// origen (query directa intra-Compras, ADR-0042 matiz intra-módulo), y
/// (2) la etiqueta del artículo (<c>ArticuloClave</c>/<c>ArticuloNombre</c>)
/// vía <see cref="IArticuloReadPort"/>. La cabecera se enriquece con la
/// etiqueta del proveedor (<see cref="IProveedorReadPort"/>). Todo en
/// <b>batch</b> (sin N+1); fallback a <c>null</c> → el frontend cae al id.
/// ADR-0042 addendum.
/// </para>
/// </summary>
public sealed class ObtenerOrdenCompraPorIdHandler
    : IRequestHandler<ObtenerOrdenCompraPorIdQuery, OrdenCompraResponse>
{
    private readonly ComprasDbContext _db;
    private readonly IMapper _mapper;
    private readonly IArticuloReadPort _articulos;
    private readonly IProveedorReadPort _proveedores;
    private readonly IDim3ReadPort _centrosCosto;

    public ObtenerOrdenCompraPorIdHandler(
        ComprasDbContext db,
        IMapper mapper,
        IArticuloReadPort articulos,
        IProveedorReadPort proveedores,
        IDim3ReadPort centrosCosto)
    {
        _db = db;
        _mapper = mapper;
        _articulos = articulos;
        _proveedores = proveedores;
        _centrosCosto = centrosCosto;
    }

    public async Task<OrdenCompraResponse> Handle(
        ObtenerOrdenCompraPorIdQuery query,
        CancellationToken cancellationToken)
    {
        // UF2-PR3-a: Include(Lineas) — collection del editor de líneas.
        // UF3-PR2: Include(Adjuntos) — collection del Tab "Adjuntos".
        // Ambas en la misma query para evitar N+1 round-trips. Sin
        // Include, EF lazy-load no aplica con AsNoTracking → las
        // collections vendrían vacías y el frontend no podría listar.
        var oc = await _db.OrdenesCompra
            .AsNoTracking()
            .Include(o => o.Lineas)
            .Include(o => o.Adjuntos)
            .FirstOrDefaultAsync(o => o.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{query.Id}' en la empresa actual.");

        var response = _mapper.Map<OrdenCompraResponse>(oc);

        // Etiqueta de proveedor (cabecera) — batch de 1 id.
        var proveedores = await _proveedores.ObtenerPorIdsAsync(
            new[] { oc.ProveedorId }, cancellationToken);
        proveedores.TryGetValue(oc.ProveedorId, out var prov);

        // Etiqueta de artículo (por línea) — batch por ids distintos.
        var articulos = await _articulos.ObtenerPorIdsAsync(
            ExtraerArticuloIdsDistintos(response.Lineas), cancellationToken);

        // Fase E PR3: CC-Máquina (Dim3) por línea — read-port público de PR1,
        // batch, SIN filtro de alcance, incluye inactivas (ADR-0050 "ver ≠
        // elegir"; ADR-0049). Un aprobador sin esa máquina en su alcance ve el
        // nombre igual; el display NO reusa el selector.
        var centrosCosto = await _centrosCosto.ObtenerAsync(
            ExtraerCentroCostoIdsDistintos(response.Lineas), cancellationToken);

        // Folio de la RQ origen por línea — query directa intra-Compras.
        var rqIds = ExtraerRequisicionIdsDistintos(response.Lineas);
        var folios = rqIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await ResolverFoliosRequisicionAsync(rqIds, cancellationToken);

        // Componer los enriquecidos de línea (folio + artículo + CC-Máquina) en
        // un solo reemplazo de la collection inmutable. Cada `with` solo toca
        // sus campos, así que no se pisan.
        var lineas = AplicarCentrosCosto(
            AplicarArticulos(
                AplicarFoliosRequisicion(response.Lineas, folios), articulos),
            centrosCosto);

        return response with
        {
            ProveedorRazonSocial = prov?.RazonSocial,
            ProveedorClave = prov?.Clave,
            Lineas = lineas,
        };
    }

    /// <summary>
    /// IDs de RQ <b>distintos</b> (sin nulos) de las líneas. Garantiza que
    /// la resolución de folios sea una sola query batch y no una por línea
    /// (varias líneas pueden venir de la misma RQ — consolidación N:1).
    /// Puro: testeable sin DB.
    /// </summary>
    internal static IReadOnlyList<Guid> ExtraerRequisicionIdsDistintos(
        IEnumerable<LineaOrdenCompraResponse> lineas) =>
        lineas
            .Where(l => l.RequisicionId is not null)
            .Select(l => l.RequisicionId!.Value)
            .Distinct()
            .ToList();

    /// <summary>
    /// IDs de artículo <b>distintos</b> de las líneas (sin N+1). Puro.
    /// </summary>
    internal static IReadOnlyCollection<Guid> ExtraerArticuloIdsDistintos(
        IEnumerable<LineaOrdenCompraResponse> lineas) =>
        lineas.Select(l => l.ArticuloId).Distinct().ToList();

    /// <summary>
    /// Reconstruye las líneas inmutables poblando <c>RequisicionFolio</c>
    /// desde el diccionario <c>rqId → folio</c>. Líneas manuales o RQ no
    /// resueltas conservan <c>RequisicionFolio = null</c> (el front cae al
    /// id). Puro: testeable sin DB.
    /// </summary>
    internal static IReadOnlyList<LineaOrdenCompraResponse> AplicarFoliosRequisicion(
        IReadOnlyList<LineaOrdenCompraResponse> lineas,
        IReadOnlyDictionary<Guid, string> foliosPorRequisicion) =>
        lineas
            .Select(l => l.RequisicionId is Guid rqId
                && foliosPorRequisicion.TryGetValue(rqId, out var folio)
                    ? l with { RequisicionFolio = folio }
                    : l)
            .ToList();

    /// <summary>
    /// Reconstruye las líneas inmutables poblando <c>ArticuloClave</c>/
    /// <c>ArticuloNombre</c> desde <c>articuloId → lectura</c>. Artículos no
    /// resueltos conservan los campos en <c>null</c> (el front cae al id).
    /// Puro: testeable sin DB.
    /// </summary>
    internal static IReadOnlyList<LineaOrdenCompraResponse> AplicarArticulos(
        IReadOnlyList<LineaOrdenCompraResponse> lineas,
        IReadOnlyDictionary<Guid, ArticuloLectura> articulos) =>
        lineas
            .Select(l => articulos.TryGetValue(l.ArticuloId, out var a)
                ? l with { ArticuloClave = a.Clave, ArticuloNombre = a.Nombre }
                : l)
            .ToList();

    /// <summary>
    /// IDs de CC-Máquina (Dim3) <b>distintos y no-null</b> de las líneas: una
    /// sola query batch al read-port (sin N+1). Puro: testeable sin DB.
    /// </summary>
    internal static IReadOnlyCollection<Guid> ExtraerCentroCostoIdsDistintos(
        IEnumerable<LineaOrdenCompraResponse> lineas) =>
        lineas
            .Where(l => l.CentroCostoId is not null)
            .Select(l => l.CentroCostoId!.Value)
            .Distinct()
            .ToList();

    /// <summary>
    /// Reconstruye las líneas poblando <c>CentroCostoClave</c>/<c>Nombre</c>
    /// desde <c>dim3Id → lectura</c>. La máquina inactiva se resuelve igual
    /// (ADR-0049: el read-port incluye inactivas). Un id sin fila (histórico
    /// irresoluble) conserva null → el FE pinta "No catalogado". Puro.
    /// </summary>
    internal static IReadOnlyList<LineaOrdenCompraResponse> AplicarCentrosCosto(
        IReadOnlyList<LineaOrdenCompraResponse> lineas,
        IReadOnlyDictionary<Guid, Dim3Lectura> centrosCosto) =>
        lineas
            .Select(l => l.CentroCostoId is Guid ccId
                    && centrosCosto.TryGetValue(ccId, out var cc)
                ? l with { CentroCostoClave = cc.Clave, CentroCostoNombre = cc.Nombre }
                : l)
            .ToList();

    /// <summary>
    /// Query directa intra-Compras: <c>rqId → folio</c> en batch.
    /// State-agnostic por construcción (sin filtro de estado). El VO
    /// <c>Folio.Valor</c> no es traducible a SQL, así que se materializa el
    /// VO en la proyección y se extrae <c>.Valor</c> en memoria — mismo
    /// patrón que <c>ComprasRequisicionReadAdapter.ObtenerFoliosAsync</c>.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> ResolverFoliosRequisicionAsync(
        IReadOnlyList<Guid> rqIds,
        CancellationToken cancellationToken)
    {
        var rows = await _db.Requisiciones
            .AsNoTracking()
            .Where(r => rqIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Folio })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.Id, x => x.Folio.Valor);
    }
}
