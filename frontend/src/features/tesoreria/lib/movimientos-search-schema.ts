import { z } from 'zod';
import {
  EstadoAplicacionMovimiento,
  EstadoConciliacionMovimiento,
  SentidoMovimiento,
} from '@/features/tesoreria/api/types';

/**
 * Search params del libro de movimientos (<c>/tesoreria/movimientos</c>,
 * P1). Todos los filtros son server-side (<c>MovimientosBancariosQuery</c>).
 */
export const MovimientosSearchSchema = z.object({
  cuentaBancariaId: z.string().optional(),
  sentido: z
    .union([
      z.literal(SentidoMovimiento.Ingreso),
      z.literal(SentidoMovimiento.Egreso),
    ])
    .optional(),
  estadoAplicacion: z
    .union([
      z.literal(EstadoAplicacionMovimiento.NoAplicado),
      z.literal(EstadoAplicacionMovimiento.AplicadoParcial),
      z.literal(EstadoAplicacionMovimiento.Aplicado),
    ])
    .optional(),
  estadoConciliacion: z
    .union([
      z.literal(EstadoConciliacionMovimiento.NoConciliado),
      z.literal(EstadoConciliacionMovimiento.Conciliado),
    ])
    .optional(),
  desde: z.string().optional(),
  hasta: z.string().optional(),
  q: z.string().optional(),
  offset: z.number().int().min(0).optional(),
  limit: z.number().int().min(1).max(500).optional(),
});

export type MovimientosSearch = z.infer<typeof MovimientosSearchSchema>;
