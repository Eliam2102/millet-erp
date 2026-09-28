import { z } from 'zod';

/**
 * Schema Zod para crear/actualizar Puesto (ADM-FE-PR1). Mirror de
 * <c>CrearPuestoValidator</c> backend (clave 1..20, nombre 1..254).
 * La clave es business key inmutable: en modo editar el input va
 * disabled y el PATCH solo manda nombre.
 *
 * <para><c>departamentoId</c> es el "departamento de referencia"
 * (opcional, informativo del catálogo maestro) — con la Parte E "un
 * puesto en varios departamentos de la sucursal" (2026-09-24) YA NO
 * es fuente de verdad de ninguna validación; el departamento real de
 * cada asignación vive en <c>SucursalPuesto</c>
 * (<c>modules/administracion/api/sucursal-puestos.ts</c>). El form
 * <c>PuestoInlineForm</c> hoy no expone este campo en la UI.</para>
 */
export const PuestoSchema = z.object({
  clave: z
    .string()
    .trim()
    .min(1, 'Clave requerida')
    .max(20, 'Máximo 20 caracteres'),
  nombre: z
    .string()
    .trim()
    .min(1, 'Nombre requerido')
    .max(254, 'Máximo 254 caracteres'),
  rolSugeridoId: z.string().nullable().optional(),
  /** Departamento de referencia (opcional, informativo). */
  departamentoId: z.string().nullable().optional(),
});

export type PuestoValues = z.infer<typeof PuestoSchema>;
