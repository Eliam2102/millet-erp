import { z } from 'zod';

/**
 * Regex laxo de UUID — formato 8-4-4-4-12 hex sin requerir RFC v1-v8.
 * Razón documentada en <c>crear-requisicion.ts</c>: el backend usa
 * GUIDs deterministas en seeds.
 */
const UUID_SHAPE_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Schema Zod del body común de <c>POST /rechazar</c>,
 * <c>POST /eliminar</c> y <c>POST /cancelar</c> (mirror de
 * <c>TerminarRequisicionRequest</c> backend). Doc 05 §13.3.
 *
 * <para><b>Reglas estructurales</b>:</para>
 * <list>
 *   <item><c>motivoId</c>: requerido, shape de UUID.</item>
 *   <item><c>motivoTexto</c>: opcional por default; el caller puede
 *   construir un schema más estricto cuando el motivo seleccionado
 *   tiene <c>permiteTextoLibre=true</c> (entonces require texto
 *   no-vacío) — ver <see cref="buildTerminarSchema"/>.</item>
 * </list>
 *
 * <para>El backend valida: motivo existe en catálogo,
 * <c>aplicaA</c> incluye el flujo (rechazar/eliminar/cancelar),
 * y <c>permiteTextoLibre</c> exige texto si está activo. Frontend
 * lo refleja para feedback inmediato.</para>
 */
export const TerminarRequisicionSchema = z.object({
  motivoId: z
    .string()
    .regex(UUID_SHAPE_RE, 'Motivo requerido'),
  motivoTexto: z.string().max(500, 'Máximo 500 caracteres').nullish(),
});

export type TerminarRequisicionValues = z.infer<
  typeof TerminarRequisicionSchema
>;

/**
 * Constructor de schema con texto libre obligatorio cuando el motivo
 * seleccionado lo exige (<c>permiteTextoLibre=true</c>). El caller lo
 * usa con <c>useForm</c> + <c>resolver</c> dinámico que se actualiza
 * cuando cambia el motivo.
 *
 * @example
 * ```ts
 * const motivoActual = motivos.find((m) => m.id === watchedMotivoId);
 * const schema = buildTerminarSchema(motivoActual?.permiteTextoLibre ?? false);
 * ```
 */
export function buildTerminarSchema(textoRequerido: boolean) {
  if (textoRequerido) {
    return z.object({
      motivoId: z.string().regex(UUID_SHAPE_RE, 'Motivo requerido'),
      motivoTexto: z
        .string()
        .min(1, 'Este motivo requiere detalle adicional')
        .max(500, 'Máximo 500 caracteres'),
    });
  }
  return TerminarRequisicionSchema;
}
