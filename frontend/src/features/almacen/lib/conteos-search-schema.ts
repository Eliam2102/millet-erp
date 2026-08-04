import { z } from 'zod';
import { EstadoConteo, TipoConteo } from '@/features/almacen/api/types';

export const ConteosSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoConteo.Planificado),
      z.literal(EstadoConteo.EnCurso),
      z.literal(EstadoConteo.EnConciliacion),
      z.literal(EstadoConteo.Aprobado),
      z.literal(EstadoConteo.Aplicado),
      z.literal(EstadoConteo.Rechazado),
    ])
    .optional(),
  tipo: z
    .union([z.literal(TipoConteo.Rotativo), z.literal(TipoConteo.Anual)])
    .optional(),
  subAlmacenId: z.string().min(1).optional(),
});

export type ConteosSearch = z.infer<typeof ConteosSearchSchema>;

export const DEFAULT_CONTEOS_SEARCH: ConteosSearch = {};
