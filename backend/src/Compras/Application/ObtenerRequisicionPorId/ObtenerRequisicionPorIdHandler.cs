using MapsterMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Ports.Administracion;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Domain.Ports.Identidad;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.ObtenerRequisicionPorId;

/// <summary>
/// Handler de <see cref="ObtenerRequisicionPorIdQuery"/>. Lookup directo
/// por <c>Id</c>; el global query filter de <c>BaseDbContext</c> aplica
/// la restricción de empresa actual (ADR-0011) y soft delete (ADR-0008)
/// automáticamente.
///
/// <para>
/// El pendiente de entregar y lo entregado se derivan del dominio
/// (<c>LineaRequisicion.CantidadPendienteEntregar</c> = (almacén+recibida) −
/// entregada, y <c>CantidadEntregada</c>) vía el mapeo de Mapster (ADR-0043
/// #3). Este handler enriquece nombres cross-módulo: requisitante y
/// departamento (ADR-0042), y la etiqueta de artículo (por línea) y
/// proveedor sugerido (cabecera) — resueltos server-side en batch para que
/// el FE no dependa de leer el catálogo completo (ADR-0042 addendum).
/// </para>
///
/// Lanza <see cref="EntityNotFoundException"/> con código
/// <c>REQUISICION_NO_ENCONTRADA</c> si el id no existe o no es accesible
/// para la empresa actual; <c>GlobalExceptionHandler</c> lo traduce a 404
/// con ProblemDetails (ADR-0010).
/// </summary>
public sealed class ObtenerRequisicionPorIdHandler : IRequestHandler<ObtenerRequisicionPorIdQuery, RequisicionResponse>
{
    private readonly ComprasDbContext _db;
    private readonly IMapper _mapper;
    private readonly IUsuarioReadPort _usuarios;
    private readonly IDepartamentoReadPort _departamentos;
    private readonly IArticuloReadPort _articulos;
    private readonly IProveedorReadPort _proveedores;
    private readonly IDim3ReadPort _centrosCosto;

    public ObtenerRequisicionPorIdHandler(
        ComprasDbContext db,
        IMapper mapper,
        IUsuarioReadPort usuarios,
        IDepartamentoReadPort departamentos,
        IArticuloReadPort articulos,
        IProveedorReadPort proveedores,
        IDim3ReadPort centrosCosto)
    {
        _db = db;
        _mapper = mapper;
        _usuarios = usuarios;
        _departamentos = departamentos;
        _articulos = articulos;
        _proveedores = proveedores;
        _centrosCosto = centrosCosto;
    }

    public async Task<RequisicionResponse> Handle(
        ObtenerRequisicionPorIdQuery query,
        CancellationToken cancellationToken)
    {
        // B.0: Include Lineas + Autorizaciones para que el FE pinte el
        // detalle (CubrimientoBar + timeline de firmas) sin queries
        // adicionales. AsSplitQuery evita blowup cartesiano del single
        // SELECT con 2 JOINs cuando hay muchas líneas y autorizaciones.
        var requisicion = await _db.Requisiciones
            .AsNoTracking()
            .Include(r => r.Lineas)
            .Include(r => r.Autorizaciones)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{query.Id}' en la empresa actual.");

        var response = _mapper.Map<RequisicionResponse>(requisicion);

        // Enrichment cross-módulo (ADR-0042): nombres de requisitante y
        // departamento resueltos en backend, para que el FE no lea los
        // catálogos completos. Batch de 1 id cada uno; fallback a null
        // (el FE cae al id) si no se resuelve.
        var nombres = await _usuarios.ObtenerNombresAsync(
            new[] { requisicion.RequisitanteId }, cancellationToken);
        var deptos = await _departamentos.ObtenerAsync(
            new[] { requisicion.DepartamentoId }, cancellationToken);
        deptos.TryGetValue(requisicion.DepartamentoId, out var depto);
        response = response with
        {
            RequisitanteNombre = nombres.GetValueOrDefault(requisicion.RequisitanteId),
            DepartamentoNombre = depto?.Nombre,
            DepartamentoClave = depto?.Clave,
            // ADR-0043: situación calculada con la MISMA función que la bandeja
            // (fuente única). Sumas en memoria sobre las líneas ya materializadas
            // (Include arriba); CantidadPendienteEntregar es computed del dominio.
            SituacionSurtido = SituacionSurtidoDerivacion.Derivar(
                requisicion.Estado,
                requisicion.Lineas.Sum(l => l.CantidadEntregada),
                requisicion.Lineas.Sum(l => l.CantidadPendienteEntregar),
                requisicion.Lineas.Sum(l => l.Cantidad)),
        };

        // ADR-0042 addendum: etiqueta de artículo (por línea) y proveedor
        // sugerido (cabecera) resueltos server-side en batch (sin N+1), para
        // que el FE no dependa de la lista capada de catálogos. Fallback a
        // null → el FE cae al id si no resuelve.
        var articulos = await _articulos.ObtenerPorIdsAsync(
            ExtraerArticuloIdsDistintos(response.Lineas), cancellationToken);
        // Fase E PR2: CC-Máquina (Dim3) resuelto por el read-port público de
        // CentrosCosto (PR1) — batch, SIN filtro de alcance, incluye inactivas
        // (ADR-0050 "ver ≠ elegir"; ADR-0049). Un aprobador sin esa máquina en
        // su alcance igual ve el nombre; el display NO reusa el selector.
        var centrosCosto = await _centrosCosto.ObtenerAsync(
            ExtraerCentroCostoIdsDistintos(response.Lineas), cancellationToken);
        var provIds = requisicion.ProveedorSugeridoId is Guid pid
            ? new[] { pid }
            : Array.Empty<Guid>();
        var proveedores = await _proveedores.ObtenerPorIdsAsync(provIds, cancellationToken);
        ProveedorLectura? prov = null;
        if (requisicion.ProveedorSugeridoId is Guid provId)
        {
            proveedores.TryGetValue(provId, out prov);
        }
        response = response with
        {
            ProveedorSugeridoRazonSocial = prov?.RazonSocial,
            ProveedorSugeridoClave = prov?.Clave,
            // Se encadenan las dos reconstrucciones: cada `with` solo toca sus
            // campos, así que artículo y CC-Máquina se pueblan sin pisarse.
            Lineas = AplicarCentrosCosto(
                AplicarArticulos(response.Lineas, articulos), centrosCosto),
        };

        return response;
    }

    /// <summary>
    /// IDs de artículo <b>distintos</b> de las líneas. Garantiza que la
    /// resolución sea una sola query batch (sin N+1) aunque varias líneas
    /// referencien el mismo artículo. Puro: testeable sin DB.
    /// </summary>
    internal static IReadOnlyCollection<Guid> ExtraerArticuloIdsDistintos(
        IEnumerable<LineaResponse> lineas) =>
        lineas.Select(l => l.ArticuloId).Distinct().ToList();

    /// <summary>
    /// Reconstruye las líneas inmutables poblando <c>ArticuloClave</c>/
    /// <c>ArticuloNombre</c> desde el diccionario <c>articuloId → lectura</c>.
    /// Artículos no resueltos conservan los campos en <c>null</c> (el FE cae
    /// al id). Puro: testeable sin DB.
    /// </summary>
    internal static IReadOnlyList<LineaResponse> AplicarArticulos(
        IReadOnlyList<LineaResponse> lineas,
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
        IEnumerable<LineaResponse> lineas) =>
        lineas
            .Where(l => l.CentroCostoId is not null)
            .Select(l => l.CentroCostoId!.Value)
            .Distinct()
            .ToList();

    /// <summary>
    /// Reconstruye las líneas poblando <c>CentroCostoClave</c>/<c>Nombre</c>
    /// desde el diccionario <c>dim3Id → lectura</c>. La máquina inactiva se
    /// resuelve igual (ADR-0049): el read-port ya incluye inactivas, así que si
    /// la clave está en el dict, se puebla sin mirar <c>Activa</c>. Un id sin
    /// fila (histórico irresoluble) conserva null → el FE pinta "No catalogado".
    /// Puro: testeable sin DB.
    /// </summary>
    internal static IReadOnlyList<LineaResponse> AplicarCentrosCosto(
        IReadOnlyList<LineaResponse> lineas,
        IReadOnlyDictionary<Guid, Dim3Lectura> centrosCosto) =>
        lineas
            .Select(l => l.CentroCostoId is Guid ccId
                    && centrosCosto.TryGetValue(ccId, out var cc)
                ? l with { CentroCostoClave = cc.Clave, CentroCostoNombre = cc.Nombre }
                : l)
            .ToList();
}
