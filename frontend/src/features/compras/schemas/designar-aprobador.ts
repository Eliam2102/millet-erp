import { z } from 'zod';
import { RolAprobador } from '@/features/compras/api/types';

/**
 * Regex laxo de UUID (8-4-4-4-12 hex sin RFC v1-v8). Razón documentada
 * en <c>crear-requisicion.ts</c>: el backend usa GUIDs deterministas
 * en seeds.
 */
const UUID_SHAPE_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Schema Zod del comando <c>POST /api/v1/compras/aprobadores</c>
 * (mirror de <c>DesignarAprobadorRequest</c>). Doc 05 §11.4.
 *
 * <para><b>Validación estructural</b>:</para>
 * <list>
 *   <item><c>departamentoId</c>, <c>usuarioId</c>: UUIDs requeridos.</item>
 *   <item><c>rol</c>: ∈ <c>RolAprobador</c> (0|1|2).</item>
 *   <item><c>motivo</c>: opcional, ≤ 500 caracteres.</item>
 * </list>
 *
 * <para>Reglas de negocio (depto existe, usuario activo en Identidad,
 * vigencia única por <c>(depto, rol)</c>) las decide el backend con
 * códigos específicos: <c>USUARIO_NO_ENCONTRADO</c> (404),
 * <c>APROBADOR_DUPLICADO</c> (422), etc.</para>
 */
export const DesignarAprobadorSchema = z.object({
  departamentoId: z
    .string()
    .regex(UUID_SHAPE_RE, 'Departamento requerido'),
  rol: z.union([
    z.literal(RolAprobador.JefeDpto),
    z.literal(RolAprobador.JefeAlmacen),
    z.literal(RolAprobador.AutorizadorN2),
  ]),
  usuarioId: z.string().regex(UUID_SHAPE_RE, 'Usuario requerido'),
  motivo: z.string().max(500, 'Máximo 500 caracteres').nullish(),
});

export type DesignarAprobadorValues = z.infer<typeof DesignarAprobadorSchema>;
