/** Presentación de los estados reales; no modifica los valores del dominio. */
export const estadosExpediente = {
  1: { tono: 'info', texto: 'Abierto' },
  2: { tono: 'neutral', texto: 'Cancelado' },
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
export const estadosSalidaOtr = {
  1: { tono: 'warning', texto: 'Pendiente' },
  2: { tono: 'info', texto: 'Entregada' },
  3: { tono: 'success', texto: 'Acusada' },
  4: { tono: 'danger', texto: 'Fallida' },
  5: { tono: 'neutral', texto: 'Reemplazada' },
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
