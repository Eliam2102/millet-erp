import { z } from 'zod';

/**
 * Schema Zod compartido para crear/actualizar Canal de venta
 * (FAC-ING-PR3). Mirror de <c>CrearCanalVentaValidator</c> backend
 * (nombre 1..254, claveAw 1..40 opcional).
 *
 * <para><c>claveAw</c> es el GRUPPE con el que A+W refiere el canal en
 * la ingesta de pedidos (ADR-0048). <c>.optional()</c> y NO
 * <c>.default('')</c>: el default hace divergir el tipo de entrada del
 * de salida y zodResolver deja de tipar con useForm (misma nota que
 * <c>SucursalSchema</c>).</para>
 */
export const CanalVentaSchema = z.object({
  nombre: z
    .string()
    .trim()
    .min(1, 'Nombre requerido')
    .max(254, 'Máximo 254 caracteres'),
  claveAw: z.string().trim().max(40, 'Máximo 40 caracteres').optional(),
});

export type CanalVentaValues = z.infer<typeof CanalVentaSchema>;
