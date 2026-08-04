namespace Millet.Compras.Domain.Ports.Almacen;

/// <summary>
/// Puerto de lectura hacia Almacén para conocer lo ya <b>entregado</b>
/// contra una requisición — las líneas de movimientos de salida tipo
/// <c>SalidaConsumo</c> en estado <c>Registrado</c> con <c>RqId = X</c>.
///
/// <para>
/// <b>Nota de terminología</b>: "entregar" es lo que hace Almacén al
/// solicitante (salida física al departamento, máquina o persona). NO
/// confundir con "surtir", que en la jerga del negocio se refiere a
/// recibir mercancía del proveedor (vía OC → recepción). El estado
/// <c>EstadoRequisicion.EnSurtido</c> usa esa segunda acepción.
/// </para>
///
/// <para>
/// <b>Consumidor:</b> el job one-time de <b>migración de históricas</b>
/// (ADR-0043 PR #4), que reconstruye el acumulador <c>CantidadEntregada</c>
/// de las RQ previas a la conmutación. (Antes de #3 lo usaba la lectura
/// read-time del detalle de RQ; #3 derivó el pendiente del dominio y dejó
/// el puerto huérfano — #4 le da consumidor real.)
/// </para>
///
/// <para>
/// El entregado se devuelve en <b>dos proyecciones</b> porque la
/// atribución por línea solo es posible cuando la salida trae
/// <c>LineaRqId</c> (poblado desde #399). Las salidas previas a #399 solo
/// tienen <c>RqId</c> a nivel cabecera del movimiento; su entregado se
/// reporta agrupado por <c>ArticuloId</c> y el clasificador decide si es
/// atribuible sin ambigüedad (artículo en una sola línea de la RQ) o no
/// (artículo repetido → no se inventa el reparto).
/// </para>
/// </summary>
public interface IAlmacenEntregasReadPort
{
    /// <summary>
    /// Para el conjunto de RQs dado, devuelve por RQ el entregado partido
    /// en dos: <see cref="EntregaReclasificacion.PorLineaRq"/> (salidas con
    /// <c>LineaRqId</c> — atribución exacta por línea) y
    /// <see cref="EntregaReclasificacion.PorArticuloSinLinea"/> (salidas sin
    /// <c>LineaRqId</c> — solo atribuibles por artículo). Solo cuenta
    /// <c>SalidaConsumo</c> en estado <c>Registrado</c>; los vales
    /// (<c>SalidaPorVale</c>) y los borradores no entran. Las RQs sin
    /// movimientos de salida no aparecen en el diccionario.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, EntregaReclasificacion>> ObtenerEntregaParaReclasificacionAsync(
        IReadOnlyCollection<Guid> requisicionIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// Entregado de una RQ partido por trazabilidad de línea, para la
/// migración de históricas (ADR-0043 PR #4). <see cref="PorLineaRq"/> suma
/// por <c>LineaRqId</c> (atribución exacta); <see cref="PorArticuloSinLinea"/>
/// suma por <c>ArticuloId</c> lo que no trae línea (atribuible exacto solo
/// si el artículo aparece en una sola línea de la RQ).
/// </summary>
public sealed record EntregaReclasificacion(
    IReadOnlyDictionary<Guid, decimal> PorLineaRq,
    IReadOnlyDictionary<Guid, decimal> PorArticuloSinLinea);
