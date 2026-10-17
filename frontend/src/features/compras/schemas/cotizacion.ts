import { z } from 'zod';
import { hoyLocalISO } from '@/lib/datetime';
import type { RegistrarCotizacionRequest } from '@/features/compras/api/types';

const FECHA_DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/;
// Regex laxo: los seeds usan GUIDs deterministas que no son UUID v1-v8 estrictos.
const UUID_SHAPE_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const LineaCotizacionSchema = z
  .object({
    lineaRequisicionId: z.string().regex(UUID_SHAPE_RE),
    /** Solo UI: unidad de la partida en la RQ; no viaja al servidor. */
    unidadRequisicion: z.string(),
    unidadCotizada: z
      .string()
      .trim()
      .min(1, 'Indica la unidad cotizada.')
      .max(10, 'La unidad admite máximo 10 caracteres.'),
    factorAUnidadRq: z
      .number({ error: 'Indica el factor de conversión.' })
      .gt(0, 'El factor de conversión debe ser mayor a cero.'),
    precioUnitario: z
      .number({ error: 'Indica el precio.' })
      .gt(0, 'El precio debe ser mayor a cero.'),
    diasEntrega: z
      .number()
      .int('Los días de entrega son un número entero.')
      .min(0, 'Los días de entrega deben estar entre 0 y 365.')
      .max(365, 'Los días de entrega deben estar entre 0 y 365.')
      .nullable(),
  })
  .superRefine((l, ctx) => {
    if (
      l.unidadCotizada.trim().toUpperCase() === l.unidadRequisicion.trim().toUpperCase() &&
      l.factorAUnidadRq !== 1
    ) {
      ctx.addIssue({
        code: 'custom',
        path: ['factorAUnidadRq'],
        message: 'Si la unidad cotizada es la misma de la requisición, el factor es 1.',
      });
    }
  });

/** Espejo de la validación del servidor (<c>RegistrarCotizacionCommand</c>); el servidor sigue siendo la barrera. */
export const cotizacionSchema = z
  .object({
    proveedorId: z.string().regex(UUID_SHAPE_RE, 'Selecciona el proveedor.'),
    folioProveedor: z.string().trim().max(40, 'El folio admite máximo 40 caracteres.'),
    moneda: z.string().regex(/^[A-Z]{3}$/, 'La moneda debe tener 3 letras, por ejemplo MXN.'),
    fechaCotizacion: z.string().regex(FECHA_DATE_ONLY_RE, 'La fecha de la cotización es requerida.'),
    vigenteHasta: z.string().regex(FECHA_DATE_ONLY_RE, 'La vigencia es requerida.'),
    condicionesCredito: z.string().trim().max(100, 'El crédito admite máximo 100 caracteres.'),
    lineas: z.array(LineaCotizacionSchema).min(1, 'La cotización debe incluir al menos una partida.'),
  })
  .superRefine((v, ctx) => {
    if (v.fechaCotizacion > hoyLocalISO()) {
      ctx.addIssue({
        code: 'custom',
        path: ['fechaCotizacion'],
        message: 'La fecha de la cotización no puede ser posterior a hoy.',
      });
    }
    if (v.vigenteHasta < v.fechaCotizacion) {
      ctx.addIssue({
        code: 'custom',
        path: ['vigenteHasta'],
        message: 'La vigencia no puede ser anterior a la fecha de la cotización.',
      });
    }
  });

export type CotizacionValues = z.infer<typeof cotizacionSchema>;

/** Body del POST: quita `unidadRequisicion` y convierte textos vacíos en `null`. */
export function aRegistrarCotizacionRequest(values: CotizacionValues): RegistrarCotizacionRequest {
  return {
    proveedorId: values.proveedorId,
    folioProveedor: values.folioProveedor.trim() || null,
    moneda: values.moneda,
    fechaCotizacion: values.fechaCotizacion,
    vigenteHasta: values.vigenteHasta,
    condicionesCredito: values.condicionesCredito.trim() || null,
    lineas: values.lineas.map((l) => ({
      lineaRequisicionId: l.lineaRequisicionId,
      unidadCotizada: l.unidadCotizada.trim().toUpperCase(),
      factorAUnidadRq: l.factorAUnidadRq,
      precioUnitario: l.precioUnitario,
      diasEntrega: l.diasEntrega,
    })),
  };
}
