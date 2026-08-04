import { z } from 'zod';
import {
  EstadoComprobacionGastos,
  TipoComprobacionGastos,
} from '@/features/cxp/api/types';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const ComprobacionesSearchSchema = z.object({
  tipo: z
    .union([
      z.literal(TipoComprobacionGastos.ReembolsoCajaChica),
      z.literal(TipoComprobacionGastos.GastosAduanales),
      z.literal(TipoComprobacionGastos.Viaticos),
      z.literal(TipoComprobacionGastos.TarjetaCredito),
    ])
    .optional(),
  estado: z
    .union([
      z.literal(EstadoComprobacionGastos.Borrador),
      z.literal(EstadoComprobacionGastos.PorRevisar),
      z.literal(EstadoComprobacionGastos.Autorizada),
      z.literal(EstadoComprobacionGastos.Aplicada),
      z.literal(EstadoComprobacionGastos.Rechazada),
      z.literal(EstadoComprobacionGastos.AutorizadaNivel1),
    ])
    .optional(),
  sucursalId: z.string().regex(UUID_RE).optional(),
});

export type ComprobacionesSearch = z.infer<typeof ComprobacionesSearchSchema>;
