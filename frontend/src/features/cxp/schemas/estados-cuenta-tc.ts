import { z } from 'zod';
import { TipoMovimientoTc } from '@/features/cxp/api/types';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/;

export const CrearEstadoCuentaTcSchema = z
  .object({
    tarjetaId: z.string().regex(UUID_RE, 'UUID tarjeta inválido.'),
    periodoDesde: z.string().regex(DATE_ONLY_RE),
    periodoHasta: z.string().regex(DATE_ONLY_RE),
    fechaCorte: z.string().regex(DATE_ONLY_RE),
    fechaLimitePago: z.string().regex(DATE_ONLY_RE),
  })
  .refine((v) => v.periodoHasta >= v.periodoDesde, {
    message: 'Periodo hasta debe ser ≥ desde.',
    path: ['periodoHasta'],
  });

export type CrearEstadoCuentaTcValues = z.infer<
  typeof CrearEstadoCuentaTcSchema
>;

export const RegistrarRefundTcSchema = z.object({
  tarjetaId: z.string().regex(UUID_RE),
  usuarioQueUsoId: z.string().regex(UUID_RE),
  fechaMovimiento: z.string().regex(DATE_ONLY_RE),
  montoOriginal: z.number().positive('Monto > 0.'),
  monedaOriginal: z.string().trim().length(3),
  tipoCambioCaptura: z.number().min(0).nullable(),
  merchantRaw: z.string().trim().min(1).max(200),
  conceptoContable: z.string().trim().min(1).max(120),
  movimientoOriginalId: z
    .string()
    .regex(UUID_RE, 'UUID del movimiento original inválido.'),
});

export type RegistrarRefundTcValues = z.infer<typeof RegistrarRefundTcSchema>;

export const RegistrarMovimientoEspecialTcSchema = z.object({
  tarjetaId: z.string().regex(UUID_RE),
  usuarioQueUsoId: z.string().regex(UUID_RE),
  fechaMovimiento: z.string().regex(DATE_ONLY_RE),
  tipo: z.union([
    z.literal(TipoMovimientoTc.GastoFinanciero),
    z.literal(TipoMovimientoTc.Anualidad),
    z.literal(TipoMovimientoTc.ComisionDivisa),
  ]),
  montoOriginal: z.number().positive('Monto > 0.'),
  monedaOriginal: z.string().trim().length(3),
  tipoCambioCaptura: z.number().min(0).nullable(),
  merchantRaw: z.string().trim().min(1).max(200),
  conceptoContable: z.string().trim().min(1).max(120),
});

export type RegistrarMovimientoEspecialTcValues = z.infer<
  typeof RegistrarMovimientoEspecialTcSchema
>;

export const DisputarMovimientoTcSchema = z.object({
  motivo: z
    .string({ error: 'Motivo obligatorio.' })
    .trim()
    .min(5, 'Mínimo 5 caracteres.')
    .max(500),
  fechaInicio: z.string().regex(DATE_ONLY_RE),
});

export type DisputarMovimientoTcValues = z.infer<
  typeof DisputarMovimientoTcSchema
>;
