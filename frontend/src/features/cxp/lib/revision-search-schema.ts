import { z } from 'zod';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Schema de los <c>search params</c> de la bandeja "Revisión por área".
 * Requiere <c>dependenciaRevisoraId</c> para listar — sin él, el
 * endpoint backend devuelve vacío.
 *
 * <para>PLATFORM-TODO(&lt;DependenciasRevisorasUsuarioContext&gt;):
 * cuando el módulo Admin asocie usuarios → dependencias, el FE puede
 * resolver automáticamente la dependencia del usuario; hoy se filtra
 * vía URL.</para>
 */
export const RevisionSearchSchema = z.object({
  dependenciaRevisoraId: z.string().regex(UUID_RE).optional(),
  motivoRevisionId: z.string().regex(UUID_RE).optional(),
});

export type RevisionSearch = z.infer<typeof RevisionSearchSchema>;
