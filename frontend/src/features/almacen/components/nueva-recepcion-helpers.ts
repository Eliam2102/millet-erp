import type { FilaRecepcionValues } from '@/features/almacen/schemas/recepcion';
import type { LineaOrdenCompraResponse } from '@/features/compras/ordenes/api/types';

/**
 * Mapea las líneas del detalle de OC a filas del form de recepción.
 * Filtra las que ya están completas (sin pendiente) — esas no aceptan
 * más recepción. El default <c>cantidad</c> es el pendiente; el
 * almacenista solo lo ajusta si lo que llegó difiere de lo solicitado.
 *
 * <para>Vive en archivo aparte para satisfacer
 * <c>react-refresh/only-export-components</c> en el sheet.</para>
 */
export function construirFilas(
  lineasOc: LineaOrdenCompraResponse[],
): FilaRecepcionValues[] {
  return lineasOc
    .map((l) => {
      const pendiente = Math.max(0, l.cantidad - l.cantidadRecibida);
      return {
        lineaOcId: l.id,
        articuloId: l.articuloId,
        articuloClave: l.articuloClave,
        articuloNombre: l.articuloNombre,
        posicion: l.posicion,
        unidadMedida: l.unidadMedida,
        cantidadSolicitada: l.cantidad,
        cantidadYaRecibida: l.cantidadRecibida,
        pendiente,
        incluida: false,
        cantidad: pendiente,
        ubicacionReferencia: null,
        ubicacionId: null,
        comentario: null,
      };
    })
    .filter((f) => f.pendiente > 0);
}

/**
 * Almacén-por-línea PR4 — helper de cabecera nivel 4.
 *
 * Índices de las filas a las que el helper DEBE auto-asignar el bin: las que
 * aún no tienen ubicación. Las que ya traen una (igual o distinta) se respetan
 * — el almacenista mandó, el helper solo rellena huecos.
 *
 * <para>Devuelve índices (no filas nuevas) para que el caller haga
 * <c>setValue(`filas.${i}.ubicacionId`)</c> y react-hook-form registre el
 * cambio por campo, sin reemplazar el array completo.</para>
 */
export function indicesParaAutoAsignar(
  filas: readonly FilaRecepcionValues[],
): number[] {
  return filas.reduce<number[]>((acc, fila, index) => {
    if (!fila.ubicacionId) acc.push(index);
    return acc;
  }, []);
}

/**
 * Cuántas filas INCLUIDAS van a una ubicación distinta del helper de cabecera.
 * Alimenta el aviso informativo "N líneas van a otra ubicación". Las filas sin
 * ubicación no cuentan (el helper ya las habrá rellenado) y sin helper el
 * conteo es 0 (no hay contra qué divergir).
 */
export function contarLineasEnOtraUbicacion(
  filas: readonly FilaRecepcionValues[],
  ubicacionHelperId: string | null | undefined,
): number {
  if (!ubicacionHelperId) return 0;
  return filas.filter(
    (f) => f.incluida && !!f.ubicacionId && f.ubicacionId !== ubicacionHelperId,
  ).length;
}
