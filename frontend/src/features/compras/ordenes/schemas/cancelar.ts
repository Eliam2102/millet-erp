import { z } from 'zod';

const UUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Schema Zod del body POST <c>/{id}/cancelar</c> (UF5-PR1, F3-PR3).
 * Cancela una OC SIN recepciones (Borrador, EnAutorizacion*, Rechazada,
 * Autorizada con `subEstadoRecepcion=SinRecepcion`).
 *
 * <para>Backend valida:</para>
 * <list>
 *   <item>Permiso <c>compras.ordenes.cancelar</c>.</item>
 *   <item>El motivo aplica al flujo de cancelación (bitmask 4 =
 *   Cancelacion).</item>
 *   <item>Si el motivo tiene <c>permiteTextoLibre=true</c>, el texto es
 *   obligatorio (≥3 chars).</item>
 * </list>
 */
export const CancelarOcSchema = z.object({
  motivoCancelacionId: z.string().regex(UUID_RE, 'Selecciona un motivo.'),
  motivoCancelacionTexto: z.string().max(500).nullish(),
});

export type CancelarOcValues = z.infer<typeof CancelarOcSchema>;
