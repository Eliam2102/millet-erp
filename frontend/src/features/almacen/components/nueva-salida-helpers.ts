import type { FilaSalidaValues } from '@/features/almacen/schemas/salida';
import type { LineaResponse } from '@/features/compras/api/types';

/**
 * Mapea las líneas del detalle de RQ a filas del form de salida.
 * Default cantidad = pendiente de <b>entregar</b>, derivado del dominio
 * (<c>cantPendienteEntregar</c> = (cantDeAlmacen + cantRecibida) −
 * cantEntregadoDeAlmacen, ADR-0043 #3). Si el backend no lo trae
 * (respuesta vieja en cache), se recalcula con la misma fórmula como
 * fallback — incluyendo <c>cantRecibida</c>, para que una línea 100%
 * compra ya recibida también sea entregable.
 *
 * <para><b>ADR-0043 #3 (conmutación):</b> NO se filtran las líneas sin
 * pendiente. Con el cierre por entrega encendido, una RQ permanece en
 * <c>EnSurtido</c> mientras se entrega en parcialidades a lo largo del
 * tiempo; el almacenista necesita ver TODAS las líneas y su avance de
 * entrega (el badge "parcial" en el sheet lo calcula con
 * entregado/solicitada). Las líneas ya entregadas por completo aparecen
 * con pendiente 0 (no accionables: <c>max</c> 0 + <c>cantidad &gt; 0</c>
 * requerido), pero quedan visibles como registro.</para>
 *
 * <para>Nota de terminología: "entregar" = salida del almacén al
 * solicitante (lo que hace este sheet). NO confundir con "surtir" que
 * en el negocio se refiere a recibir mercancía del proveedor (vía OC
 * + recepción).</para>
 *
 * <para>Vive en archivo aparte para satisfacer
 * <c>react-refresh/only-export-components</c> en el sheet.</para>
 */
/**
 * Avance de entrega de una línea, derivado de lo entregado vs la
 * cantidad <b>original solicitada</b> (ADR-0043 #3). Alimenta el badge
 * del sheet de salida:
 * <list>
 *   <item><c>sin-entregar</c>: nada entregado aún (sin badge).</item>
 *   <item><c>parcial</c>: 0 &lt; entregado &lt; solicitada.</item>
 *   <item><c>entregada</c>: entregado ≥ solicitada (cubierta del todo).</item>
 * </list>
 */
export type EstadoEntregaLinea = 'sin-entregar' | 'parcial' | 'entregada';

export function clasificarEntrega(
  entregado: number,
  solicitada: number,
): EstadoEntregaLinea {
  if (entregado <= 0) return 'sin-entregar';
  if (entregado >= solicitada) return 'entregada';
  return 'parcial';
}

export function construirFilasDeRq(
  lineasRq: LineaResponse[],
): FilaSalidaValues[] {
  return lineasRq.map((l) => {
    const yaEntregada = l.cantEntregadoDeAlmacen ?? 0;
    const pendiente =
      l.cantPendienteEntregar ??
      Math.max(0, l.cantDeAlmacen + l.cantRecibida - yaEntregada);
    return {
      lineaRqId: l.id,
      articuloId: l.articuloId,
      articuloClave: l.articuloClave,
      articuloNombre: l.articuloNombre,
      posicion: l.posicion,
      unidadMedida: l.unidadMedida,
      cantidadSolicitada: l.cantidad,
      cantidadPlaneadaAlmacen: l.cantDeAlmacen,
      cantidadYaEntregada: yaEntregada,
      pendienteEntregar: pendiente,
      incluida: false,
      cantidad: pendiente,
      // Fase E PR5 Camino 1: el CC-Máquina se hereda de la línea de RQ y se
      // muestra bloqueado (read-only). Clave/nombre para el display (la RQ
      // LineaResponse los expone resueltos desde PR2).
      centroCostoId: l.centroCostoId,
      centroCostoClave: l.centroCostoClave,
      centroCostoNombre: l.centroCostoNombre,
      proyectoId: null,
      ubicacionReferencia: null,
      ubicacionId: null,
      comentario: null,
    };
  });
}
