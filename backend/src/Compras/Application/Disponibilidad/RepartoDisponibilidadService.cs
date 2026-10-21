using Millet.Compras.Domain;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Compras.Application.Disponibilidad;

/// <summary>
/// Reparto estimado de una línea de requisición entre lo que cubre el almacén
/// de la sucursal (<see cref="DeAlmacen"/>) y lo que hay que comprar
/// (<see cref="DeCompra"/>). Invariante: <c>DeAlmacen + DeCompra = Linea.Cantidad</c>.
/// <see cref="Disponible"/> es el stock disponible de la línea expresado en la
/// unidad de documento (ya truncado a los decimales del documento). Para una
/// línea de servicio <see cref="Disponible"/> es 0 y <see cref="EsServicio"/> es
/// <c>true</c>.
/// </summary>
public sealed record RepartoLinea(
    LineaRequisicion Linea,
    decimal Disponible,
    decimal DeAlmacen,
    decimal DeCompra,
    bool EsServicio);

/// <summary>
/// Cálculo de reparto por línea compartido por la matriz de aprobación
/// (<c>EvaluadorMultidimensional</c>), la bandeja de pendientes
/// (<c>ListarPendientesAutorizacionHandler</c>), el preview de cubrimiento
/// y la autorización (<c>AutorizarRequisicionHandler</c>, que solo añade el
/// apartado de existencia encima). Mismo cálculo en todos: stock por sucursal,
/// conversión a unidad de documento, truncado a los decimales del documento y
/// descuento acumulado por artículo entre líneas del mismo artículo. Solo
/// lectura: no reserva stock.
///
/// <para>
/// Regla de servicio (minuta Millet 12-oct), única fuente: si la RQ es de
/// clasificación <c>Servicio</c> o la línea es de un artículo <c>EsServicio</c>,
/// la línea va completa a compra (<c>DeAlmacen = 0</c>, <c>DeCompra = Cantidad</c>)
/// <b>sin llamar a <see cref="IConsultarStockPort"/></b> ni descontar acumulados.
/// </para>
/// </summary>
public static class RepartoDisponibilidadService
{
    /// <summary>Reparto por línea, en el mismo orden de <paramref name="lineas"/>. Una consulta de stock por requisición (lote).</summary>
    public static async Task<IReadOnlyList<RepartoLinea>> RepartirAsync(
        IConsultarStockPort stock,
        IConversionUnidadPort conversion,
        IReadOnlyDictionary<Guid, ArticuloLectura> articulos,
        Clasificacion clasificacionRq,
        Guid sucursalId,
        IEnumerable<LineaRequisicion> lineas,
        CancellationToken cancellationToken)
    {
        var lineasLista = lineas.ToList();
        var idsInventario = lineasLista
            .Where(l => clasificacionRq != Clasificacion.Servicio && !l.EsActivoFijo && !EsServicio(l, articulos))
            .Select(l => l.ArticuloId)
            .Distinct()
            .ToList();
        var stockPorArticulo = idsInventario.Count > 0
            ? await stock.ConsultarPorSucursalLoteAsync(sucursalId, idsInventario, cancellationToken)
            : new Dictionary<Guid, DisponibilidadStock>();

        var resultado = new List<RepartoLinea>();
        var disponiblesEnBase = new Dictionary<Guid, decimal>();
        foreach (var linea in lineasLista)
        {
            if (linea.EsActivoFijo || clasificacionRq == Clasificacion.Servicio || EsServicio(linea, articulos))
            {
                resultado.Add(new RepartoLinea(linea, 0m, 0m, linea.Cantidad, EsServicio: !linea.EsActivoFijo));
                continue;
            }

            var disponibilidad = stockPorArticulo.GetValueOrDefault(linea.ArticuloId) ?? new DisponibilidadStock(0m, 0m);
            var conversionLinea = await conversion.ConvertirAsync(
                linea.ArticuloId, linea.Cantidad, null, linea.UnidadMedida, cancellationToken);
            var disponibleBase = disponiblesEnBase.GetValueOrDefault(linea.ArticuloId, disponibilidad.Disponible);
            var escala = (decimal)Math.Pow(10, Math.Min(4, conversionLinea.DecimalesDocumento));
            var disponibleDocumento = decimal.Floor(disponibleBase / conversionLinea.FactorDocumentoABase * escala) / escala;
            var (deAlmacen, deCompra) = Cubrimiento.Repartir(disponibleDocumento, linea.Cantidad);
            disponiblesEnBase[linea.ArticuloId] = disponibleBase - deAlmacen * conversionLinea.FactorDocumentoABase;
            resultado.Add(new RepartoLinea(linea, disponibleDocumento, deAlmacen, deCompra, EsServicio: false));
        }

        return resultado;
    }

    /// <summary>
    /// <c>true</c> si la línea es de un artículo de servicio. Los servicios no
    /// tienen existencia: no cuentan como faltante de almacén.
    /// </summary>
    public static bool EsServicio(LineaRequisicion linea, IReadOnlyDictionary<Guid, ArticuloLectura> articulos) =>
        articulos.TryGetValue(linea.ArticuloId, out var articulo) && articulo.EsServicio;
}
