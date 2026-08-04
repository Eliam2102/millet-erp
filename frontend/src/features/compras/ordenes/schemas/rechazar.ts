import { z } from 'zod';

const UUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Schema Zod del body del POST <c>/{id}/rechazar</c> (UF4-PR1).
 * El backend valida adicionalmente:
 * <list>
 *   <item>El motivo aplica al flujo de OC (bitmask 8).</item>
 *   <item>El permiso requerido según el estado actual de la OC
 *   (<c>EnAutorizacionJefeCompras</c> → <c>autorizar-nivel1</c>;
 *   <c>EnAutorizacionDireccion</c> → <c>autorizar-nivel2</c>).</item>
 *   <item>Si el motivo tiene <c>permiteTextoLibre=true</c>, el texto
 *   es obligatorio (≥3 chars).</item>
 * </list>
 */
export const RechazarOcSchema = z.object({
  motivoRechazoId: z.string().regex(UUID_RE, 'Selecciona un motivo.'),
  motivoRechazoTexto: z.string().max(500).nullish(),
  notas: z.string().max(500).nullish(),
});

export type RechazarOcValues = z.infer<typeof RechazarOcSchema>;
