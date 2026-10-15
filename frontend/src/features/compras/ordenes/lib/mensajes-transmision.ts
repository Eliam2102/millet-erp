import { esApiError, mensajeErrorAccion } from '@/lib/api/error';

/**
 * Mensajes legibles para los códigos 422 que devuelve
 * <c>POST /{id}/transmitir</c> cuando una OC de importación se envía a
 * autorización sin sus datos de importación (la captura se permite en
 * borrador y se exige al transmitir).
 */
const MENSAJES_TRANSMISION: Readonly<Record<string, string>> = {
  IMPORT_INCOTERM_REQUERIDO:
    'Falta el incoterm de la operación. Captúralo en la pestaña Información, sub-pestaña Importación, antes de enviar la OC a autorización.',
  IMPORT_PAIS_REQUERIDO:
    'Falta el país de origen de la operación. Captúralo en la pestaña Información, sub-pestaña Importación, antes de enviar la OC a autorización.',
};

/** Mensaje para el usuario al fallar «Transmitir a autorización». */
export function mensajeErrorTransmision(error: unknown): string {
  const code = esApiError(error) ? error.code : undefined;
  return (code && MENSAJES_TRANSMISION[code]) || mensajeErrorAccion(error);
}
