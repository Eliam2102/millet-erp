import { z } from 'zod';
import { cabeEnDecimales, DECIMALES_FALLBACK } from '@/components/erp/forms/decimales-unidad';

import { zId } from '@/lib/z-id';
const idLike = (mensaje: string) => zId(mensaje);
// Vacío ('') se normaliza a null: el campo es opcional y no debe fallar con GUID inválido.
const idLikeOpcional = z.union([zId(), z.literal('').transform(() => null)]).nullish();

/**
 * Factory del schema Zod de "agregar/actualizar línea" (doc 05 §11.2).
 *
 * <para><b>Decimales por unidad (ADR-0046 Etapa 2, PR-2c, advisory):</b>
 * <paramref name="getDecimalesCantidad"/> devuelve los decimales permitidos por
 * la unidad del artículo seleccionado; default = fallback global (5) cuando la
 * unidad no resuelve (FK null / string sin match). El refine lo lee en tiempo de
 * validación (closure), así el resolver no se recrea al cambiar de artículo. El
 * backend valida autoritativo de todos modos.</para>
 */
export function crearLineaSchema(getDecimalesCantidad: () => number = () => DECIMALES_FALLBACK) {
  return z
    .object({
      esActivoFijo: z.boolean().optional(),
      claseActivoId: zId().nullish(),
      articuloId: idLike('Artículo requerido'),
      cantidad: z
        .number({ message: 'Cantidad requerida' })
        .positive('La cantidad debe ser mayor a 0')
        .refine(
          (v) => cabeEnDecimales(v, getDecimalesCantidad()),
          'Demasiados decimales para la unidad seleccionada',
        ),
      unidadMedida: z.string().min(1, 'Unidad de medida requerida').max(20, 'Máximo 20 caracteres'),
      // Precio estimado opcional (minuta Millet 12-oct): la RQ no lleva monto;
      // sin precio el backend guarda 0 MXN. Negativo sigue rechazado.
      precioEstimadoMonto: z
        .number({ message: 'Precio estimado inválido' })
        .nonnegative('El precio no puede ser negativo')
        .refine((v) => cabeEnDecimales(v, 2), 'Máximo 2 decimales en el precio')
        .nullish(),
      precioEstimadoMoneda: z
        .string()
        .length(3, 'La moneda debe tener 3 letras, por ejemplo MXN')
        .regex(/^[A-Z]{3}$/, 'Código de moneda en mayúsculas')
        .nullish(),
      cuentaContableId: idLikeOpcional,
      // ADM08: centro heredado o elegido requerido; máquina opcional.
      centroCostoId: idLike('Centro de costo requerido'),
      proyecto: z.string().max(50, 'Máximo 50 caracteres').nullish(),
      fechaRequerida: z.iso.date().nullish(),
      notas: z.string().max(500, 'Máximo 500 caracteres').nullish(),
    })
    .superRefine((data, ctx) => {
      if (data.esActivoFijo && !data.claseActivoId)
        ctx.addIssue({
          code: 'custom',
          path: ['claseActivoId'],
          message: 'Selecciona la clase de activo.',
        });
      if (data.esActivoFijo && !Number.isInteger(data.cantidad))
        ctx.addIssue({
          code: 'custom',
          path: ['cantidad'],
          message: 'Los activos requieren unidades enteras.',
        });
    });
}

/** Schema estático con el fallback global (usos que no resuelven unidad). */
export const LineaSchema = crearLineaSchema();

export type LineaValues = z.infer<typeof LineaSchema>;
