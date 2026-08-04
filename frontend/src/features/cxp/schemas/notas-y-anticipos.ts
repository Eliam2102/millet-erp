import { z } from 'zod';
import {
  TipoNotaCredito,
  TipoRelacionCfdi,
} from '@/features/cxp/api/types';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/;
const UUID_CFDI_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const CapturarNotaCreditoSchema = z.object({
  cfdiRecibidoId: z.string().regex(UUID_RE).nullable(),
  uuidCfdi: z.string().regex(UUID_CFDI_RE, 'UUID CFDI inválido.'),
  proveedorId: z.string().regex(UUID_RE, 'UUID proveedor inválido.'),
  folioProveedor: z.string().trim().max(50).nullable(),
  serieProveedor: z.string().trim().max(20).nullable(),
  fechaCfdi: z.string().regex(DATE_ONLY_RE, 'Formato YYYY-MM-DD.'),
  moneda: z.string().trim().length(3),
  tipoCambio: z.number().min(0).nullable(),
  subtotal: z.number().min(0),
  impuestosTrasladados: z.number().min(0),
  retenciones: z.number().min(0),
  total: z.number().positive('Total debe ser > 0.'),
  tipo: z.union([
    z.literal(TipoNotaCredito.Descuento),
    z.literal(TipoNotaCredito.Devolucion),
    z.literal(TipoNotaCredito.AmortizacionAnticipo),
  ]),
  tipoRelacionCfdi: z.union([
    z.literal(TipoRelacionCfdi.NotaCredito),
    z.literal(TipoRelacionCfdi.Devolucion),
    z.literal(TipoRelacionCfdi.AmortizacionAnticipo),
  ]),
  uuidRelacionCfdi: z
    .string()
    .regex(UUID_CFDI_RE, 'UUID del CFDI relacionado inválido.'),
});

export type CapturarNotaCreditoValues = z.infer<
  typeof CapturarNotaCreditoSchema
>;

export const CapturarAnticipoSchema = z.object({
  cfdiRecibidoId: z.string().regex(UUID_RE).nullable(),
  uuidCfdi: z.string().regex(UUID_CFDI_RE, 'UUID CFDI inválido.'),
  proveedorId: z.string().regex(UUID_RE, 'UUID proveedor inválido.'),
  serie: z
    .string()
    .trim()
    .min(1, 'Serie obligatoria (típicamente FANT).')
    .max(20),
  folioProveedor: z.string().trim().max(50).nullable(),
  fechaCfdi: z.string().regex(DATE_ONLY_RE, 'Formato YYYY-MM-DD.'),
  moneda: z.string().trim().length(3),
  tipoCambio: z.number().min(0).nullable(),
  montoEntregado: z.number().positive('Monto debe ser > 0.'),
  ordenCompraId: z.string().regex(UUID_RE).nullable(),
});

export type CapturarAnticipoValues = z.infer<typeof CapturarAnticipoSchema>;

export const CrearNotaCargoSchema = z.object({
  proveedorId: z.string().regex(UUID_RE, 'UUID proveedor inválido.'),
  sucursalId: z.string().regex(UUID_RE).nullable(),
  concepto: z
    .string()
    .trim()
    .min(5, 'Concepto mínimo 5 caracteres.')
    .max(500),
  conceptoContableId: z.string().regex(UUID_RE).nullable(),
  monto: z.number().positive('Monto debe ser > 0.'),
  moneda: z.string().trim().length(3),
  tipoCambio: z.number().min(0).nullable(),
  facturaOrigenId: z.string().regex(UUID_RE).nullable(),
  devolucionAProveedorId: z.string().regex(UUID_RE).nullable(),
});

export type CrearNotaCargoValues = z.infer<typeof CrearNotaCargoSchema>;

export const AplicarNcAFacturaSchema = z.object({
  notaCreditoId: z.string().regex(UUID_RE, 'Selecciona una NC.'),
  notaCreditoVersionEsperada: z.number().int().nonnegative(),
  monto: z.number().positive('Monto debe ser > 0.'),
});

export type AplicarNcAFacturaValues = z.infer<typeof AplicarNcAFacturaSchema>;

export const AplicarAnticipoAFacturaSchema = z.object({
  anticipoId: z.string().regex(UUID_RE, 'Selecciona un anticipo.'),
  anticipoVersionEsperada: z.number().int().nonnegative(),
  monto: z.number().positive('Monto debe ser > 0.'),
});

export type AplicarAnticipoAFacturaValues = z.infer<
  typeof AplicarAnticipoAFacturaSchema
>;
