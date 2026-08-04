import { z } from 'zod';

/**
 * Schemas Zod de los comandos de gestión de CFDIs (FE-F1-PR1). Validan
 * client-side los inputs de los sheets antes de enviar al backend.
 *
 * <para>El motivo del descartar es libre (texto). El backend lo
 * almacena para auditoría.</para>
 */

/**
 * Regex laxo de UUID — formato <c>8-4-4-4-12</c> hex sin requerir
 * RFC 4122 v1-v8. Hoy los CFDIs nacen con <c>Guid.CreateVersion7()</c>
 * válido, pero usamos el patrón consistente del resto del repo
 * (<c>UUID_SHAPE_RE</c> en compras/almacen) por si alguien añade
 * seeds deterministas a futuro.
 */
const UUID_SHAPE_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const DescartarCfdiSchema = z.object({
  motivo: z
    .string({ error: 'El motivo es obligatorio.' })
    .trim()
    .min(5, 'Mínimo 5 caracteres.')
    .max(500, 'Máximo 500 caracteres.'),
});

export type DescartarCfdiValues = z.infer<typeof DescartarCfdiSchema>;

export const MarcarCfdiDuplicadoSchema = z.object({
  cfdiOriginalId: z
    .string({ error: 'Debes indicar el CFDI original.' })
    .regex(UUID_SHAPE_RE, 'Debe ser un identificador válido (UUID).'),
});

export type MarcarCfdiDuplicadoValues = z.infer<
  typeof MarcarCfdiDuplicadoSchema
>;
