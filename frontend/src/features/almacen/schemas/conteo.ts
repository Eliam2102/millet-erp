import { z } from 'zod';
import { TipoConteo } from '@/features/almacen/api/types';

const UUID_SHAPE_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/;

export const CrearConteoSchema = z.object({
  tipo: z.union([
    z.literal(TipoConteo.Rotativo),
    z.literal(TipoConteo.Anual),
  ]),
  fechaPlanificada: z
    .string()
    .regex(DATE_ONLY_RE, 'Fecha en formato YYYY-MM-DD'),
  responsableId: z.string().regex(UUID_SHAPE_RE, 'Responsable requerido'),
  subAlmacenId: z.string().regex(UUID_SHAPE_RE).nullish(),
  filtroFamilia: z.string().max(50, 'Máximo 50 caracteres').nullish(),
});

export type CrearConteoValues = z.infer<typeof CrearConteoSchema>;

export const CapturarLineaSchema = z.object({
  cantidadReal: z
    .number({ message: 'Cantidad requerida' })
    .nonnegative('La cantidad no puede ser negativa'),
});

export type CapturarLineaValues = z.infer<typeof CapturarLineaSchema>;
