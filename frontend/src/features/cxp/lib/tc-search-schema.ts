import { z } from 'zod';
import {
  EstadoCuentaTcStatus,
  EstadoMovimientoTc,
  EstadoTarjeta,
  TipoMovimientoTc,
} from '@/features/cxp/api/types';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/;

export const TarjetasSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoTarjeta.Activa),
      z.literal(EstadoTarjeta.Bloqueada),
      z.literal(EstadoTarjeta.Cancelada),
    ])
    .optional(),
  titularId: z.string().regex(UUID_RE).optional(),
});

export type TarjetasSearch = z.infer<typeof TarjetasSearchSchema>;

export const MovimientosTcSearchSchema = z.object({
  tarjetaId: z.string().regex(UUID_RE).optional(),
  usuarioQueUsoId: z.string().regex(UUID_RE).optional(),
  estado: z
    .union([
      z.literal(EstadoMovimientoTc.Registrado),
      z.literal(EstadoMovimientoTc.ConciliadoConEstadoCuenta),
      z.literal(EstadoMovimientoTc.EnDisputa),
      z.literal(EstadoMovimientoTc.Reversado),
      z.literal(EstadoMovimientoTc.PagadoAlBanco),
    ])
    .optional(),
  tipo: z
    .union([
      z.literal(TipoMovimientoTc.CompraConCfdi),
      z.literal(TipoMovimientoTc.CompraSinCfdi),
      z.literal(TipoMovimientoTc.Refund),
      z.literal(TipoMovimientoTc.GastoFinanciero),
      z.literal(TipoMovimientoTc.Anualidad),
      z.literal(TipoMovimientoTc.ComisionDivisa),
    ])
    .optional(),
  fechaDesde: z.string().regex(DATE_ONLY_RE).optional(),
  fechaHasta: z.string().regex(DATE_ONLY_RE).optional(),
});

export type MovimientosTcSearch = z.infer<typeof MovimientosTcSearchSchema>;

export const EstadosCuentaTcSearchSchema = z.object({
  tarjetaId: z.string().regex(UUID_RE).optional(),
  estado: z
    .union([
      z.literal(EstadoCuentaTcStatus.EnConciliacion),
      z.literal(EstadoCuentaTcStatus.Conciliado),
      z.literal(EstadoCuentaTcStatus.Cerrado),
      z.literal(EstadoCuentaTcStatus.PagadoBanco),
    ])
    .optional(),
});

export type EstadosCuentaTcSearch = z.infer<
  typeof EstadosCuentaTcSearchSchema
>;
