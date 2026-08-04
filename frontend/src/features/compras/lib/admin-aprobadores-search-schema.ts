import { z } from 'zod';

/**
 * Schema de los <c>search params</c> de <c>/compras/admin/aprobadores</c>.
 * Doc 05 §11.4. Persistir filtros en URL para compartir links a una
 * vista específica del histórico (ej. "todos los cambios del depto X").
 *
 * <para><b>Tab</b>: <c>vigentes</c> (default) o <c>historico</c>. Los
 * filtros departamento/rol/usuario aplican a ambas tabs (la tab activa
 * decide qué endpoint pegar).</para>
 */
export const AdminAprobadoresSearchSchema = z.object({
  tab: z.enum(['vigentes', 'historico']).catch('vigentes'),
  departamentoId: z.string().optional(),
  rol: z.coerce.number().int().min(0).max(2).optional(),
  usuarioId: z.string().optional(),
});

export type AdminAprobadoresSearch = z.infer<
  typeof AdminAprobadoresSearchSchema
>;

export const DEFAULT_ADMIN_APROBADORES_SEARCH: AdminAprobadoresSearch = {
  tab: 'vigentes',
};
