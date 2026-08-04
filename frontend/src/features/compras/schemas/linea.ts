import { z } from 'zod';
import {
  cabeEnDecimales,
  DECIMALES_FALLBACK,
} from '@/components/erp/forms/decimales-unidad';

/**
 * Regex laxo de UUID — formato <c>8-4-4-4-12</c> hex sin requerir
 * version/variant válidos (RFC 4122 v1-v8). Razón: el backend usa
 * GUIDs deterministas (ej. <c>00000005-0001-0000-0000-000000000001</c>)
 * que NO califican como UUID v1-v8 pero son válidos como string opaco.
 * Ver nota completa en <c>crear-requisicion.ts</c>.
 */
const UUID_SHAPE_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const idLike = (mensajeRequerido: string) =>
  z.string().regex(UUID_SHAPE_RE, mensajeRequerido);

const idLikeOpcional = z.string().regex(UUID_SHAPE_RE).nullish();

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
export function crearLineaSchema(
  getDecimalesCantidad: () => number = () => DECIMALES_FALLBACK,
) {
  return z.object({
    articuloId: idLike('Artículo requerido'),
    cantidad: z
      .number({ message: 'Cantidad requerida' })
      .positive('La cantidad debe ser mayor a 0')
      .refine(
        (v) => cabeEnDecimales(v, getDecimalesCantidad()),
        'Demasiados decimales para la unidad seleccionada',
      ),
    unidadMedida: z
      .string()
      .min(1, 'Unidad de medida requerida')
      .max(20, 'Máximo 20 caracteres'),
    precioEstimadoMonto: z
      .number({ message: 'Precio estimado requerido' })
      .positive('El precio debe ser mayor a 0')
      .refine((v) => cabeEnDecimales(v, 2), 'Máximo 2 decimales en el precio'),
    precioEstimadoMoneda: z
      .string()
      .length(3, 'Código de moneda ISO 4217 (3 letras)')
      .regex(/^[A-Z]{3}$/, 'Código de moneda en mayúsculas'),
    cuentaContableId: idLikeOpcional,
    // Fase E PR2.1: el CC-Máquina pasa de opcional a REQUERIDO en la línea de RQ.
    centroCostoId: idLike('CC-Máquina requerido'),
    proyecto: z.string().max(50, 'Máximo 50 caracteres').nullish(),
    fechaRequerida: z.iso.date().nullish(),
    notas: z.string().max(500, 'Máximo 500 caracteres').nullish(),
  });
}

/** Schema estático con el fallback global (usos que no resuelven unidad). */
export const LineaSchema = crearLineaSchema();

export type LineaValues = z.infer<typeof LineaSchema>;
