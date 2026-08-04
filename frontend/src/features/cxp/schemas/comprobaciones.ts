import { z } from 'zod';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/;
const UUID_CFDI_RE = UUID_RE;

export const CajaChicaLineaSchema = z.object({
  cfdiRecibidoId: z.string().regex(UUID_RE).nullable(),
  uuidCfdi: z.string().regex(UUID_CFDI_RE, 'UUID CFDI inválido.').nullable(),
  proveedorId: z.string().regex(UUID_RE, 'UUID proveedor inválido.'),
  folioProveedor: z.string().trim().max(40).nullable(),
  serieProveedor: z.string().trim().max(25).nullable(),
  fechaCfdi: z.string().regex(DATE_ONLY_RE, 'Formato YYYY-MM-DD.'),
  subtotal: z.number().min(0),
  descuentos: z.number().min(0),
  impuestosTrasladados: z.number().min(0),
  retenciones: z.number().min(0),
  total: z.number().positive('Total > 0.'),
  fechaVencimiento: z.string().regex(DATE_ONLY_RE, 'Formato YYYY-MM-DD.'),
  concepto: z.string().trim().max(500).nullable(),
});

export const CrearComprobacionCajaChicaSchema = z.object({
  sucursalId: z.string().regex(UUID_RE, 'UUID sucursal inválido.'),
  responsableId: z.string().regex(UUID_RE, 'UUID responsable inválido.'),
  fechaInicio: z.string().regex(DATE_ONLY_RE),
  fechaFin: z.string().regex(DATE_ONLY_RE),
  moneda: z.string().trim().length(3),
  observaciones: z.string().trim().max(500).nullable(),
  // GI-PR4 (doc 12 Q1): a quién se repone — cuenta de sucursal (1) o
  // responsable de la caja (2).
  destinoReposicion: z.union([z.literal(1), z.literal(2)]),
  cfdis: z
    .array(CajaChicaLineaSchema)
    .min(1, 'Agrega al menos un CFDI.')
    .max(50, 'Máximo 50 CFDIs por comprobación.'),
});

export type CrearComprobacionCajaChicaValues = z.infer<
  typeof CrearComprobacionCajaChicaSchema
>;

export const CrearComprobacionAduanalesSchema = z.object({
  sucursalId: z.string().regex(UUID_RE, 'UUID sucursal inválido.'),
  responsableId: z.string().regex(UUID_RE, 'UUID responsable inválido.'),
  proveedorId: z.string().regex(UUID_RE, 'UUID proveedor (agencia) inválido.'),
  numeroPedimento: z
    .string()
    .trim()
    .min(1, 'Número de pedimento obligatorio.')
    .max(40),
  fechaInicio: z.string().regex(DATE_ONLY_RE),
  fechaFin: z.string().regex(DATE_ONLY_RE),
  moneda: z.string().trim().length(3),
  observaciones: z.string().trim().max(500).nullable(),
  facturaProveedorIds: z
    .array(z.string().regex(UUID_RE, 'UUID factura inválido.'))
    .min(1, 'Agrega al menos un UUID de factura ya capturada.'),
});

export type CrearComprobacionAduanalesValues = z.infer<
  typeof CrearComprobacionAduanalesSchema
>;

export const RechazarComprobacionSchema = z.object({
  motivo: z
    .string({ error: 'Motivo obligatorio.' })
    .trim()
    .min(5, 'Mínimo 5 caracteres.')
    .max(500),
});

export type RechazarComprobacionValues = z.infer<
  typeof RechazarComprobacionSchema
>;
