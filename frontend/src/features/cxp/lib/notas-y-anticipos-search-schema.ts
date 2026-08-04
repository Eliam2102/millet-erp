import { z } from 'zod';
import {
  EstadoAnticipo,
  EstadoNotaCargo,
  EstadoNotaCredito,
} from '@/features/cxp/api/types';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const NotasCreditoSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoNotaCredito.EnEspera),
      z.literal(EstadoNotaCredito.Abierta),
      z.literal(EstadoNotaCredito.Aplicada),
      z.literal(EstadoNotaCredito.Cancelada),
    ])
    .optional(),
  proveedorId: z.string().regex(UUID_RE).optional(),
});

export type NotasCreditoSearch = z.infer<typeof NotasCreditoSearchSchema>;

export const AnticiposSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoAnticipo.Abierto),
      z.literal(EstadoAnticipo.Amortizado),
      z.literal(EstadoAnticipo.Cancelado),
    ])
    .optional(),
  proveedorId: z.string().regex(UUID_RE).optional(),
});

export type AnticiposSearch = z.infer<typeof AnticiposSearchSchema>;

export const NotasCargoSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoNotaCargo.Borrador),
      z.literal(EstadoNotaCargo.Autorizada),
      z.literal(EstadoNotaCargo.Aplicada),
      z.literal(EstadoNotaCargo.Formalizada),
      z.literal(EstadoNotaCargo.Cancelada),
    ])
    .optional(),
  proveedorId: z.string().regex(UUID_RE).optional(),
});

export type NotasCargoSearch = z.infer<typeof NotasCargoSearchSchema>;
