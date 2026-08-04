import { z } from 'zod';

/**
 * Schema Zod para asociar un grupo de Entra ID a un rol. Mirror de
 * <c>AsociarGrupoEntraIdValidator</c> backend.
 *
 * <para><b>ObjectId</b>: 1..100 caracteres. Es el GUID del grupo en
 * Microsoft Entra ID. El backend lo valida como string (sin regex
 * estricto de GUID) para permitir mocks/sandboxes. Nosotros aceptamos
 * cualquier string no vacío hasta 100 chars; el usuario lo copia desde
 * el portal de Entra.</para>
 *
 * <para><b>Nombre</b>: 1..254 (display name del grupo).</para>
 */

export const AsociarGrupoEntraIdSchema = z.object({
  objectId: z
    .string()
    .trim()
    .min(1, 'ObjectId requerido')
    .max(100, 'Máximo 100 caracteres'),
  nombre: z
    .string()
    .trim()
    .min(1, 'Nombre del grupo requerido')
    .max(254, 'Máximo 254 caracteres'),
});

export type AsociarGrupoEntraIdValues = z.infer<
  typeof AsociarGrupoEntraIdSchema
>;
