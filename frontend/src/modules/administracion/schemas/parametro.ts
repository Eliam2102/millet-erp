import { z } from 'zod';
import { TipoParametro } from '@/modules/administracion/api/types';

/**
 * Schema Zod del PATCH /admin/parametros/{clave} (F-Admin-PR7.1).
 *
 * <para>El validador es <b>dinámico</b>: el backend valida que el
 * string entrante parsee según el <c>tipo</c> declarado en la fila;
 * el frontend replica esa validación localmente para dar feedback
 * inmediato sin viaje al servidor. Si el backend evoluciona la regla
 * (p.ej. permitir nulos, formato extra), acá se sincroniza.</para>
 *
 * <list>
 *   <item><c>Texto</c>  → string no vacío.</item>
 *   <item><c>Numero</c> → string parseable como número finito.</item>
 *   <item><c>Booleano</c> → "true" / "false" literales (case-sensitive
 *         para alinear con <c>bool.TryParse</c> en el backend, que
 *         acepta solo esos dos).</item>
 *   <item><c>Json</c>   → string que pasa <c>JSON.parse</c>.</item>
 * </list>
 */
export function actualizarParametroSchema(tipo: TipoParametro) {
  return z.object({
    valor: z
      .string()
      .max(2000, 'Máximo 2000 caracteres')
      .superRefine((valor, ctx) => {
        switch (tipo) {
          case TipoParametro.Texto: {
            if (valor.trim().length === 0) {
              ctx.addIssue({
                code: z.ZodIssueCode.custom,
                message: 'El valor no puede estar vacío.',
              });
            }
            return;
          }
          case TipoParametro.Numero: {
            if (valor.trim().length === 0) {
              ctx.addIssue({
                code: z.ZodIssueCode.custom,
                message: 'Ingresa un número.',
              });
              return;
            }
            const n = Number(valor);
            if (!Number.isFinite(n)) {
              ctx.addIssue({
                code: z.ZodIssueCode.custom,
                message: 'No es un número válido.',
              });
            }
            return;
          }
          case TipoParametro.Booleano: {
            if (valor !== 'true' && valor !== 'false') {
              ctx.addIssue({
                code: z.ZodIssueCode.custom,
                message: 'Solo "true" o "false".',
              });
            }
            return;
          }
          case TipoParametro.Json: {
            if (valor.trim().length === 0) {
              ctx.addIssue({
                code: z.ZodIssueCode.custom,
                message: 'Ingresa un JSON válido.',
              });
              return;
            }
            try {
              JSON.parse(valor);
            } catch {
              ctx.addIssue({
                code: z.ZodIssueCode.custom,
                message: 'JSON inválido.',
              });
            }
            return;
          }
        }
      }),
  });
}

export type ActualizarParametroValues = z.infer<
  ReturnType<typeof actualizarParametroSchema>
>;
