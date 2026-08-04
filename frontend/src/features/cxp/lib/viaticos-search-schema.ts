import { z } from 'zod';
import { EstadoSolicitudViaticos } from '@/features/cxp/api/types';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const ViaticosSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoSolicitudViaticos.Solicitada),
      z.literal(EstadoSolicitudViaticos.AutorizadaPorJefe),
      z.literal(EstadoSolicitudViaticos.RequiereDireccionFinanzas),
      z.literal(EstadoSolicitudViaticos.AutorizadaCompleta),
      z.literal(EstadoSolicitudViaticos.Anticipada),
      z.literal(EstadoSolicitudViaticos.ComprobacionCapturada),
      z.literal(EstadoSolicitudViaticos.Liquidada),
      z.literal(EstadoSolicitudViaticos.Rechazada),
    ])
    .optional(),
  empleadoId: z.string().regex(UUID_RE).optional(),
  jefeDirectoId: z.string().regex(UUID_RE).optional(),
});

export type ViaticosSearch = z.infer<typeof ViaticosSearchSchema>;
