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
  OC_COTIZACION_REQUERIDA:
    'Adjunta la cotización en la pestaña Adjuntos antes de enviar la orden a autorización. El correo de autorización no la sustituye.',
  OC_SIN_RQ_CORREO_REQUERIDO:
    'Esta orden no tiene requisición previa. Adjunta el correo de autorización en la pestaña Adjuntos antes de enviarla.',
  OC_MOTIVO_SIN_RQ_REQUERIDO:
    'Esta orden no tiene requisición previa. Captura el motivo en la pestaña Información antes de enviarla a autorización.',
  OC_TRANSMITIR_BORRADOR_MINIMO:
    'Faltan datos de la cabecera: proveedor, condiciones de pago o uso principal. Complétalos en la pestaña Información antes de enviar la orden.',
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
