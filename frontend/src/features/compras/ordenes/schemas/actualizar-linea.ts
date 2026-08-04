import { z } from 'zod';
import { DescuentoTipo } from '@/features/compras/ordenes/api/types';

/**
 * Schema Zod del comando <c>PATCH /api/v1/compras/ordenes/{id}/lineas/{lineaId}</c>
 * (UF2-PR3 — actualizar línea). Mirror del shape esperado por el
 * backend <c>ActualizarLineaOcCommand</c>. PATCH parcial: cualquier
 * campo nullable que llegue como <c>null</c> NO se modifica; los
 * flags <c>limpiarX</c> setean a null explícitamente.
 *
 * <para>Para líneas con <c>requisicionId</c> ≠ null (heredadas de RQ),
 * el backend rechaza cambios en cantidad/articuloId — solo el precio
 * y campos no estructurales son editables. La UI debe pre-validar
 * para feedback inmediato (deshabilitar inputs no-editables).</para>
 */
export const ActualizarLineaSchema = z
  .object({
    articuloId: z.string().nullish(),
    cantidad: z
      .number()
      .positive('La cantidad debe ser mayor a 0.')
      .max(999_999_999)
      .nullish(),
    unidadMedida: z.string().min(1).max(20).nullish(),
    precioUnitario: z
      .number()
      .nonnegative('El precio no puede ser negativo.')
      .max(999_999_999)
      .nullish(),
    descuentoTipo: z
      .union([
        z.literal(DescuentoTipo.Porcentaje),
        z.literal(DescuentoTipo.Monto),
      ])
      .nullish(),
    descuentoValor: z
      .number()
      .nonnegative('El descuento no puede ser negativo.')
      .nullish(),
    indicadorImpuestos: z.string().max(50).nullish(),
    departamentoSolicitanteId: z.string().nullish(),
    descripcionExtendida: z.string().max(1000).nullish(),
    fechaEntregaLinea: z.string().nullish(),
    limpiarDescripcionExtendida: z.boolean().nullish(),
    limpiarFechaEntregaLinea: z.boolean().nullish(),
    // Fase E PR3: solo para líneas MANUALES. En heredadas el backend lo rechaza
    // (LINEA_OC_CC_HEREDADO_INMUTABLE); el FE ni lo manda (campo read-only).
    centroCostoId: z.string().nullish(),
  })
  .superRefine((data, ctx) => {
    if (
      data.descuentoTipo === DescuentoTipo.Porcentaje &&
      data.descuentoValor != null &&
      data.descuentoValor > 100
    ) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['descuentoValor'],
        message: 'El porcentaje no puede exceder 100.',
      });
    }
  });

export type ActualizarLineaValues = z.infer<typeof ActualizarLineaSchema>;
