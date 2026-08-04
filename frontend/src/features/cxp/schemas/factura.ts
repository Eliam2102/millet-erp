import { z } from 'zod';
import { MotivoCancelacion } from '@/features/cxp/api/types';

/**
 * Schemas Zod para captura y mutaciones de Factura de Proveedor
 * (FE-F2-PR1). Validan client-side los inputs antes de enviar al
 * backend (FluentValidation re-valida ahí).
 *
 * <para>Evitamos <c>z.coerce.number()</c> porque rompe la inferencia
 * de tipos con RHF (input <c>unknown</c> vs output <c>number</c>);
 * usamos <c>z.number()</c> + <c>register(..., {valueAsNumber: true})</c>.</para>
 */

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/;

export const CapturarFacturaLineaSchema = z.object({
  /** Vínculo opcional al catálogo de artículos (DatosMaestros). */
  articuloId: z.string().regex(UUID_RE, 'Artículo inválido.').nullable(),
  descripcion: z
    .string()
    .trim()
    .min(1, 'Descripción obligatoria.')
    .max(500, 'Máximo 500 caracteres.'),
  claveProdServ: z.string().trim().max(20).nullable(),
  cantidad: z.number().positive('Cantidad debe ser > 0.'),
  claveUnidad: z
    .string()
    .trim()
    .min(1, 'Clave de unidad obligatoria.')
    .max(20),
  unidad: z.string().trim().max(50).nullable(),
  precioUnitario: z.number().min(0, 'Precio debe ser ≥ 0.'),
  importe: z.number().min(0, 'Importe debe ser ≥ 0.'),
  descuento: z.number().min(0).nullable(),
  lineaOcId: z
    .string()
    .regex(UUID_RE, 'UUID inválido.')
    .nullable(),
});

export type CapturarFacturaLineaValues = z.infer<
  typeof CapturarFacturaLineaSchema
>;

export const CapturarFacturaSchema = z.object({
  // La OC se elige con el selector; proveedor y sucursal se DERIVAN de la OC
  // (read-only en el form). Siguen siendo UUID requeridos en el payload para
  // mantener intacto el contrato del backend y su validación OC_PROVEEDOR_MISMATCH.
  ordenCompraId: z
    .string()
    .min(1, 'Selecciona una orden de compra.')
    .regex(UUID_RE, 'Orden de compra inválida.'),
  proveedorId: z
    .string()
    .min(1, 'Selecciona una OC para derivar el proveedor.')
    .regex(UUID_RE, 'Proveedor inválido.'),
  sucursalId: z
    .string()
    .min(1, 'Selecciona una OC para derivar la sucursal.')
    .regex(UUID_RE, 'Sucursal inválida.'),
  cfdiRecibidoId: z.string().regex(UUID_RE, 'UUID de CFDI inválido.').nullable(),
  uuidCfdi: z.string().trim().max(36).nullable(),
  folioProveedor: z.string().trim().max(50).nullable(),
  serieProveedor: z.string().trim().max(20).nullable(),
  fechaDocumento: z.string().regex(DATE_ONLY_RE, 'Formato YYYY-MM-DD.'),
  fechaContabilizacion: z.string().regex(DATE_ONLY_RE, 'Formato YYYY-MM-DD.'),
  fechaVencimiento: z.string().regex(DATE_ONLY_RE, 'Formato YYYY-MM-DD.'),
  moneda: z
    .string()
    .trim()
    .length(3, 'Código ISO 3 caracteres (MXN/USD/EUR).'),
  tipoCambio: z.number().min(0).nullable(),
  subtotal: z.number().min(0),
  descuentos: z.number().min(0),
  impuestosTrasladados: z.number().min(0),
  retenciones: z.number().min(0),
  total: z.number().positive('Total debe ser > 0.'),
  lineas: z
    .array(CapturarFacturaLineaSchema)
    .min(1, 'Agrega al menos una línea.')
    .max(200, 'Máximo 200 líneas por factura.'),
});

export type CapturarFacturaValues = z.infer<typeof CapturarFacturaSchema>;

export const CancelarFacturaSchema = z
  .object({
    motivo: z.union([
      z.literal(MotivoCancelacion.RechazadaPorTolerancia),
      z.literal(MotivoCancelacion.CfdiCanceladoEnSat),
      z.literal(MotivoCancelacion.ErrorCaptura),
      z.literal(MotivoCancelacion.OtroConTexto),
    ]),
    texto: z.string().trim().max(500).nullable(),
  })
  .refine(
    (v) =>
      v.motivo !== MotivoCancelacion.OtroConTexto ||
      (v.texto != null && v.texto.length >= 5),
    {
      message: 'El motivo "Otro" requiere texto explicativo (≥ 5 caracteres).',
      path: ['texto'],
    },
  );

export type CancelarFacturaValues = z.infer<typeof CancelarFacturaSchema>;
