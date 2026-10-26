import type { FilaRecepcionValues } from '@/features/almacen/schemas/recepcion';
import type { LineaOrdenCompraResponse } from '@/features/compras/ordenes/api/types';
import type { LineaRecibible } from '@/features/almacen/api/types';

/**
 * Mapea las líneas del detalle de OC a filas del form de recepción.
 * Omite los servicios (CA2.10) y filtra las que ya están completas (sin pendiente) — esas no aceptan
 * más recepción. El default <c>cantidad</c> es el pendiente; el
 * almacenista solo lo ajusta si lo que llegó difiere de lo solicitado.
 *
 * <para><paramref name="recibibles"/> es la respuesta del servidor
 * (<c>useLineasRecibibles</c>): de ahí salen el pendiente y el
 * máximo recibible (igual al pendiente). Si una línea no aparece en ese índice
 * (defensivo), se calcula local.</para>
 *
 * <para>Vive en archivo aparte para satisfacer
 * <c>react-refresh/only-export-components</c> en el sheet.</para>
 */
export function construirFilas(
  lineasOc: LineaOrdenCompraResponse[],
  recibibles: readonly LineaRecibible[],
): FilaRecepcionValues[] {
  const porLinea = new Map(recibibles.map((r) => [r.lineaOcId, r]));
  return lineasOc
    .filter((l) => !l.esServicio) // CA2.10: los servicios no se reciben en Almacén
    .map((l) => {
      const r = porLinea.get(l.id);
      const pendiente = r ? r.pendiente : Math.max(0, l.cantidad - l.cantidadRecibida);
      return {
        fraccionArancelaria: l.fraccionArancelaria ?? null,
        lineaOcId: l.id,
        esActivoFijo: l.esActivoFijo ?? false,
        articuloId: l.articuloId,
        articuloClave: l.articuloClave,
        articuloNombre: l.articuloNombre,
        posicion: l.posicion,
        unidadMedida: l.unidadMedida,
        cantidadSolicitada: l.cantidad,
        cantidadYaRecibida: r ? r.cantidadRecibida : l.cantidadRecibida,
        pendiente,
        maximo: r ? r.maximoRecibible : pendiente,
        incluida: false,
        // Con medida (E1) la cantidad se captura en hojas enteras: por defecto, las hojas pendientes.
        cantidad: r?.medida ? hojasPendientes(pendiente, r.medida.areaHojaM2) : pendiente,
        medida: r?.medida ?? null,
        requiereMedida: r?.requiereMedida ?? false,
        ubicacionReferencia: null,
        ubicacionId: null,
        comentario: null,
        cantidadDanada: 0,
        motivoDano: null,
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
  /** Si se pasa, solo se rellenan las filas que lo cumplen (artículo asignado al rack). */
  puedeAsignar?: (fila: FilaRecepcionValues) => boolean,
): number[] {
  return filas.reduce<number[]>((acc, fila, index) => {
    if (!fila.esActivoFijo && !fila.ubicacionId && (puedeAsignar?.(fila) ?? true)) acc.push(index);
    return acc;
  }, []);
}

/**
 * Invariante «una recepción = un sub-almacén» (RECEPCION_MULTI_SUBALMACEN):
 * el sub de referencia es el de la primera fila incluida con rack; las demás
 * filas incluidas cuyo rack es de otro sub-almacén quedan en <c>conflictos</c>
 * (índice de fila → clave del sub de referencia).
 */
export function detectarConflictoSubAlmacen(
  filas: readonly FilaRecepcionValues[],
  subPorUbicacion: ReadonlyMap<string, { id: string; clave: string }>,
): { subBaseClave: string | null; conflictos: Set<number> } {
  let base: { id: string; clave: string } | null = null;
  const conflictos = new Set<number>();
  filas.forEach((f, i) => {
    if (!f.incluida || !f.ubicacionId) return;
    const sub = subPorUbicacion.get(f.ubicacionId);
    if (!sub) return;
    if (!base) base = sub;
    else if (sub.id !== base.id) conflictos.add(i);
  });
  return { subBaseClave: (base as { clave: string } | null)?.clave ?? null, conflictos };
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
  return filas.filter((f) => f.incluida && !!f.ubicacionId && f.ubicacionId !== ubicacionHelperId)
    .length;
}

/** Hojas enteras que faltan por recibir de una línea con medida (el pendiente en m² son hojas enteras por construcción). */
export const hojasPendientes = (pendienteM2: number, areaHojaM2: number) =>
  Math.floor(pendienteM2 / areaHojaM2 + 1e-6);
