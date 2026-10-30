/**
 * Resultado de una aprobación masiva ya enriquecido con el folio, porque el
 * API solo devuelve <c>id</c>. Lo arma la página al aprobar, no al refrescar.
 */
export interface ResultadoAprobacionItem {
  id: string;
  folio: string;
  ok: boolean;
  mensaje: string | null;
}

/**
 * Convierte la respuesta de <c>autorizaciones-masivas</c> en items con folio.
 * Si el id no está en el mapa (no debería pasar) muestra el id como folio.
 */
export function resultadosConFolio(
  resultados: readonly { id: string; ok: boolean; mensaje: string | null }[],
  folioPorId: ReadonlyMap<string, string>,
): ResultadoAprobacionItem[] {
  return resultados.map((x) => ({
    id: x.id,
    folio: folioPorId.get(x.id) ?? '—',
    ok: x.ok,
    mensaje: x.mensaje,
  }));
}
