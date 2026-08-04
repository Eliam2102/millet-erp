import { z } from 'zod';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/;
const ULTIMOS_4_RE = /^\d{4}$/;

export const CrearTarjetaSchema = z.object({
  emisora: z.string().trim().min(1).max(60),
  perfilParser: z.string().trim().min(1).max(40),
  ultimosCuatro: z.string().regex(ULTIMOS_4_RE, '4 dígitos.'),
  nombreAlias: z.string().trim().min(1).max(120),
  titularId: z.string().regex(UUID_RE, 'UUID titular inválido.'),
  bancoProveedorId: z.string().regex(UUID_RE, 'UUID banco inválido.'),
  limiteCreditoMxn: z.number().positive('Límite > 0.'),
  monedaDefault: z.string().trim().length(3),
  diaCorte: z.number().int().min(1).max(31),
  diaLimitePago: z.number().int().min(1).max(60),
  vigenciaDesde: z.string().regex(DATE_ONLY_RE),
});

export type CrearTarjetaValues = z.infer<typeof CrearTarjetaSchema>;

export const ActualizarTarjetaSchema = z.object({
  nombreAlias: z.string().trim().min(1).max(120),
  limiteCreditoMxn: z.number().positive('Límite > 0.'),
  diaCorte: z.number().int().min(1).max(31),
  diaLimitePago: z.number().int().min(1).max(60),
});

export type ActualizarTarjetaValues = z.infer<typeof ActualizarTarjetaSchema>;

export const AgregarUsuarioAutorizadoSchema = z.object({
  empleadoId: z.string().regex(UUID_RE, 'UUID empleado inválido.'),
  vigenciaDesde: z.string().regex(DATE_ONLY_RE),
  vigenciaHasta: z.string().regex(DATE_ONLY_RE).nullable(),
  montoMaxMensualMxn: z.number().positive().nullable(),
});

export type AgregarUsuarioAutorizadoValues = z.infer<
  typeof AgregarUsuarioAutorizadoSchema
>;

export const MovimientoTcSinCfdiSchema = z.object({
  tarjetaId: z.string().regex(UUID_RE),
  usuarioQueUsoId: z.string().regex(UUID_RE),
  fechaMovimiento: z.string().regex(DATE_ONLY_RE),
  montoOriginal: z.number().positive('Monto > 0.'),
  monedaOriginal: z.string().trim().length(3),
  tipoCambioCaptura: z.number().min(0).nullable(),
  merchantRaw: z.string().trim().min(1).max(200),
  descripcionLibre: z.string().trim().max(500).nullable(),
  conceptoContable: z.string().trim().min(1).max(120),
  ticketBlobRef: z.string().trim().max(200).nullable(),
});

export type MovimientoTcSinCfdiValues = z.infer<
  typeof MovimientoTcSinCfdiSchema
>;

export const MovimientoTcConCfdiSchema = z.object({
  tarjetaId: z.string().regex(UUID_RE),
  usuarioQueUsoId: z.string().regex(UUID_RE),
  fechaMovimiento: z.string().regex(DATE_ONLY_RE),
  montoOriginal: z.number().positive('Monto > 0.'),
  monedaOriginal: z.string().trim().length(3),
  tipoCambioCaptura: z.number().min(0).nullable(),
  merchantRaw: z.string().trim().min(1).max(200),
  descripcionLibre: z.string().trim().max(500).nullable(),
  conceptoContable: z.string().trim().min(1).max(120),
  cfdiRecibidoId: z.string().regex(UUID_RE, 'UUID CFDI inválido.'),
  proveedorId: z.string().regex(UUID_RE, 'UUID proveedor inválido.'),
  uuidCfdi: z.string().regex(UUID_RE).nullable(),
  folioProveedor: z.string().trim().max(50).nullable(),
  serieProveedor: z.string().trim().max(20).nullable(),
  fechaCfdi: z.string().regex(DATE_ONLY_RE),
  subtotal: z.number().min(0),
  descuentos: z.number().min(0),
  impuestosTrasladados: z.number().min(0),
  retenciones: z.number().min(0),
  totalFactura: z.number().positive('Total > 0.'),
  fechaVencimiento: z.string().regex(DATE_ONLY_RE),
});

export type MovimientoTcConCfdiValues = z.infer<
  typeof MovimientoTcConCfdiSchema
>;
