using Millet.Compras.Domain.Ports.Almacen;

namespace Millet.Compras.Application.MigracionEntrega;

/// <summary>
/// Línea de una RQ candidata a reclasificación, con lo mínimo que el
/// clasificador necesita: su id (para atribuir por <c>LineaRqId</c>), su
/// artículo (para atribuir lo que no trae línea) y la cantidad original
/// solicitada (el techo del entregado y el umbral de "totalmente entregada").
/// </summary>
public sealed record LineaParaReclasificar(Guid LineaId, Guid ArticuloId, decimal Cantidad);

/// <summary>
/// Veredicto del clasificador para una RQ.
/// <list type="bullet">
///   <item><see cref="EsAmbigua"/>: ≥1 artículo aparece en &gt;1 línea de la RQ
///     y tiene entrega sin <c>LineaRqId</c> → no se puede repartir sin inventar.
///     El job NO toca estas RQ (van a la lista de revisión manual).</item>
///   <item><see cref="EntregadoPorLinea"/>: entregado atribuido por línea,
///     <b>capado al techo</b> (<c>Cantidad</c> de la línea). Solo válido si
///     <c>!EsAmbigua</c>.</item>
///   <item><see cref="TotalmenteEntregada"/>: todas las líneas alcanzaron su
///     <c>Cantidad</c> (entrega completa real). Decide el cubo: una RQ Cerrada
///     totalmente entregada se queda Cerrada; si no, vuelve a EnSurtido.</item>
/// </list>
/// </summary>
public sealed record ResultadoReclasificacion(
    bool EsAmbigua,
    IReadOnlyDictionary<Guid, decimal> EntregadoPorLinea,
    bool TotalmenteEntregada);

/// <summary>
/// Lógica pura de reclasificación de una RQ histórica (ADR-0043 PR #4). Dado
/// el detalle de líneas y el entregado leído de Almacén (partido por
/// <c>LineaRqId</c> / por artículo sin línea), decide si la atribución es
/// <b>exacta</b> o <b>ambigua</b> y, si es exacta, cuánto se entregó por línea
/// (capado al techo) y si la RQ está totalmente entregada.
///
/// <para>
/// <b>Regla de ambigüedad (el riesgo del PR, ADR-0043 §punto 2):</b> el
/// surtido es por línea. Si un artículo está en una sola línea de la RQ, todo
/// lo entregado de ese artículo es de esa línea — atribución exacta aunque la
/// salida sea pre-#399 (sin <c>LineaRqId</c>). Solo cuando el mismo artículo
/// está en ≥2 líneas <b>y</b> hay entrega sin <c>LineaRqId</c> el reparto es
/// indecidible: NO se inventa, se marca ambigua.
/// </para>
///
/// <para>
/// <b>Cap al techo:</b> el entregado por línea se capa a <c>Cantidad</c>. Una
/// RQ histórica puede tener sobre-entrega (más salidas que lo solicitado);
/// escribir el valor crudo dejaría <c>CantidadEntregada &gt; (almacén+recibida)</c>
/// y la fórmula de pendiente de #401 daría negativo. Capado ⇒ pendiente ≥ 0.
/// </para>
/// </summary>
public static class ReclasificacionEntregaCalculator
{
    public static ResultadoReclasificacion Clasificar(
        IReadOnlyCollection<LineaParaReclasificar> lineas,
        EntregaReclasificacion entrega)
    {
        var grupos = lineas.GroupBy(l => l.ArticuloId).ToList();

        // ¿Ambigua? algún artículo en >1 línea con entrega sin LineaRqId.
        var esAmbigua = grupos.Any(g =>
            g.Count() > 1 && entrega.PorArticuloSinLinea.GetValueOrDefault(g.Key) > 0m);

        if (esAmbigua)
        {
            return new ResultadoReclasificacion(
                EsAmbigua: true,
                EntregadoPorLinea: new Dictionary<Guid, decimal>(),
                TotalmenteEntregada: false);
        }

        var entregadoPorLinea = new Dictionary<Guid, decimal>(lineas.Count);
        var totalmenteEntregada = true;

        foreach (var grupo in grupos)
        {
            var lineasArticulo = grupo.ToList();
            // sinLinea solo es exactamente atribuible cuando el artículo está
            // en UNA sola línea (si estuviera en varias con sinLinea>0 ya
            // habríamos retornado ambigua arriba).
            var sinLinea = entrega.PorArticuloSinLinea.GetValueOrDefault(grupo.Key);

            foreach (var linea in lineasArticulo)
            {
                var crudo = entrega.PorLineaRq.GetValueOrDefault(linea.LineaId);
                if (lineasArticulo.Count == 1)
                {
                    crudo += sinLinea;
                }

                var capado = Math.Min(crudo, linea.Cantidad);
                entregadoPorLinea[linea.LineaId] = capado;

                if (capado < linea.Cantidad)
                {
                    totalmenteEntregada = false;
                }
            }
        }

        return new ResultadoReclasificacion(
            EsAmbigua: false,
            EntregadoPorLinea: entregadoPorLinea,
            TotalmenteEntregada: totalmenteEntregada);
    }
}
