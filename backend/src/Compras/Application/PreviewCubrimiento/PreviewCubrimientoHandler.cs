using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.PreviewCubrimiento;

/// <summary>
/// Handler de <see cref="PreviewCubrimientoQuery"/>. <b>Read-only puro</b>:
/// carga la RQ con <c>AsNoTracking</c>, y si está <c>EnAutorizacion</c>,
/// consulta <see cref="IConsultarStockPort"/> por línea y reparte con la
/// función pura <see cref="Cubrimiento.Repartir"/> — la <b>misma</b> que usa
/// la bifurcación real al autorizar, sin drift.
///
/// <para><b>Invariantes</b>: NUNCA escribe (no <c>SaveChanges</c>) — no reserva
/// stock (PR4/ADR-0047: ya no existen reservas) ni escribe las columnas de
/// cubrimiento (que siguen en 0 hasta que la bifurcación real corra al
/// autorizar). El resultado es una estimación al instante de la consulta.</para>
///
/// <para>Lanza <see cref="EntityNotFoundException"/>
/// (<c>REQUISICION_NO_ENCONTRADA</c> → 404) si el id no existe o no es
/// accesible para la empresa actual (global query filter, ADR-0011). Si la RQ
/// no está <c>EnAutorizacion</c>, devuelve <c>Aplica = false</c> con lista
/// vacía (no es error).</para>
/// </summary>
public sealed class PreviewCubrimientoHandler
    : IRequestHandler<PreviewCubrimientoQuery, PreviewCubrimientoResponse>
{
    private readonly ComprasDbContext _db;
    private readonly IConsultarStockPort _stock;
    private readonly IArticuloReadPort _articulos;

    public PreviewCubrimientoHandler(
        ComprasDbContext db,
        IConsultarStockPort stock,
        IArticuloReadPort articulos)
    {
        _db = db;
        _stock = stock;
        _articulos = articulos;
    }

    public async Task<PreviewCubrimientoResponse> Handle(
        PreviewCubrimientoQuery query,
        CancellationToken cancellationToken)
    {
        var requisicion = await _db.Requisiciones
            .AsNoTracking()
            .Include(r => r.Lineas)
            .FirstOrDefaultAsync(r => r.Id == query.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{query.RequisicionId}' en la empresa actual.");

        // El preview solo tiene sentido en EnAutorizacion (antes de la
        // bifurcación real). En otros estados el cubrimiento ya está persistido.
        if (requisicion.Estado != EstadoRequisicion.EnAutorizacion)
        {
            return new PreviewCubrimientoResponse(
                requisicion.Id,
                Aplica: false,
                Lineas: Array.Empty<PreviewCubrimientoLinea>());
        }

        var lineas = new List<PreviewCubrimientoLinea>(requisicion.Lineas.Count);
        foreach (var linea in requisicion.Lineas)
        {
            // Solo CONSULTA (lectura). El reparto lo hace la función pura.
            var disponibilidad = await _stock.ConsultarPorSucursalAsync(
                requisicion.SucursalId,
                linea.ArticuloId,
                cancellationToken);

            var (deAlmacen, deCompra) =
                Cubrimiento.Repartir(disponibilidad.Disponible, linea.Cantidad);

            lineas.Add(new PreviewCubrimientoLinea(
                LineaId: linea.Id,
                ArticuloId: linea.ArticuloId,
                ArticuloClave: null,
                ArticuloNombre: null,
                Cantidad: linea.Cantidad,
                EstimadoDeAlmacen: deAlmacen,
                EstimadoDeCompra: deCompra,
                Disponible: disponibilidad.Disponible));
        }

        // Enriquecimiento de etiqueta de artículo por línea, server-side en
        // batch (ADR-0042 addendum): el panel "Disponibilidad estimada" no debe
        // depender de la lista capada de catálogos. Fallback a null → el FE cae
        // al id. Mismo patrón que ObtenerRequisicionPorIdHandler.
        var articulos = await _articulos.ObtenerPorIdsAsync(
            ExtraerArticuloIdsDistintos(lineas), cancellationToken);

        return new PreviewCubrimientoResponse(
            requisicion.Id, Aplica: true, AplicarArticulos(lineas, articulos));
    }

    /// <summary>
    /// IDs de artículo <b>distintos</b> de las líneas → una sola query batch
    /// (sin N+1) aunque varias líneas referencien el mismo artículo. Puro.
    /// </summary>
    internal static IReadOnlyCollection<Guid> ExtraerArticuloIdsDistintos(
        IEnumerable<PreviewCubrimientoLinea> lineas) =>
        lineas.Select(l => l.ArticuloId).Distinct().ToList();

    /// <summary>
    /// Puebla <c>ArticuloClave</c>/<c>ArticuloNombre</c> por línea desde el
    /// diccionario <c>articuloId → lectura</c>. Los no resueltos conservan
    /// <c>null</c> (el FE cae al id). Puro.
    /// </summary>
    internal static IReadOnlyList<PreviewCubrimientoLinea> AplicarArticulos(
        IReadOnlyList<PreviewCubrimientoLinea> lineas,
        IReadOnlyDictionary<Guid, ArticuloLectura> articulos) =>
        lineas
            .Select(l => articulos.TryGetValue(l.ArticuloId, out var a)
                ? l with { ArticuloClave = a.Clave, ArticuloNombre = a.Nombre }
                : l)
            .ToList();
}
