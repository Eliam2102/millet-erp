import { esApiError, mensajeErrorAccion } from '@/lib/api/error';

/**
 * Mensajes legibles para los códigos 422 que devuelve
 * <c>POST /{id}/transmitir</c> cuando una OC de importación se envía a
 * autorización sin sus datos de importación (la captura se permite en
 * borrador y se exige al transmitir).
 */
const MENSAJES_TRANSMISION: Readonly<Record<string, string>> = {
  OC_FICHA_TECNICA_REQUERIDA:
    'Adjunta la ficha técnica de la orden antes de enviarla a autorización.',
  IMPORT_INCOTERM_REQUERIDO:
    'Falta el incoterm de la operación. Captúralo en la pestaña Información, sub-pestaña Importación, antes de enviar la OC a autorización.',
  IMPORT_PAIS_REQUERIDO:
    'Falta el país de origen de la operación. Captúralo en la pestaña Información, sub-pestaña Importación, antes de enviar la OC a autorización.',
  // El servidor nombra las líneas sin precio; el texto de aquí es solo fallback.
  OC_PRECIO_REQUERIDO:
    'Captura el precio de las líneas sin precio antes de enviar a autorización.',
};

/** Mensaje para el usuario al fallar «Transmitir a autorización». */
export function mensajeErrorTransmision(error: unknown): string {
  const code = esApiError(error) ? error.code : undefined;
  if (code === 'OC_PRECIO_REQUERIDO' && esApiError(error) && error.problem.detail) {
    return error.problem.detail;
  }
  return (code && MENSAJES_TRANSMISION[code]) || mensajeErrorAccion(error);
}
