/** Presentación de los estados reales; no modifica los valores del dominio. */
export const estadosExpediente = {
  1: { tono: 'info', texto: 'Abierto' },
  2: { tono: 'neutral', texto: 'Cancelado' },
  3: { tono: 'success', texto: 'Cerrado' },
} as const;
export const estadosOtr = {
  1: { tono: 'warning', texto: 'Por vincular' },
  2: { tono: 'info', texto: 'Vinculado' },
  3: { tono: 'neutral', texto: 'Cancelado' },
} as const;
export const estadosPartidaOtr = {
  1: { tono: 'warning', texto: 'Pendiente' },
  2: { tono: 'info', texto: 'Propuesta' },
  3: { tono: 'warning', texto: 'Requiere revisión' },
} as const;
export const estadosContenedor = {
  1: { tono: 'info', texto: 'Activo' },
  2: { tono: 'neutral', texto: 'Cancelado' },
} as const;
export const estadosPartida = {
  1: { tono: 'info', texto: 'Vigente' },
  2: { tono: 'warning', texto: 'Revisión: pedido modificado en A+W' },
  3: { tono: 'neutral', texto: 'Liberada por cancelación del expediente' },
} as const;
export const estadosDocumento = {
  vigente: { tono: 'info', texto: 'Vigente' },
  reemplazada: { tono: 'neutral', texto: 'Reemplazada' },
  noAplica: { tono: 'neutral', texto: 'No aplica' },
} as const;
export const estadosHito = {
  omitido: { tono: 'neutral', texto: 'Omitido' },
  registrado: { tono: 'success', texto: 'Registrado' },
  atrasado: { tono: 'danger', texto: 'Con atraso' },
  pendiente: { tono: 'warning', texto: 'Pendiente' },
} as const;
/** Traducción de la leyenda técnica existente, sólo al presentar el dato. */
export function textoFinanciero(texto: string) {
  return texto === 'Pendiente · se habilita con CE-03'
    ? 'Pendiente de habilitar el seguimiento financiero'
    : texto;
}

const nombresCierre: Record<string, string> = {
  fisico: 'cierre físico',
  documental: 'cierre documental',
  financiero: 'cierre financiero',
};
/** La aprobación de Gerencia exige los tres cierres registrados y sin faltantes (dominio: AprobarCierre). */
export function cierresPendientesDeAprobar(
  cierres: ReadonlyArray<{
    dimension: string;
    fecha?: string | null;
    faltantes: readonly string[];
  }>,
) {
  return cierres
    .filter((c) => !c.fecha || c.faltantes.length > 0)
    .map((c) => nombresCierre[c.dimension] ?? c.dimension);
}
