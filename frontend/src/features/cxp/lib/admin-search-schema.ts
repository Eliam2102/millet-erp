import { z } from 'zod';
import {
  TipoDestinoViatico,
  TipoGastoAprobador,
} from '@/features/cxp/api/types';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const AprobadoresLimitesSearchSchema = z.object({
  empleadoId: z.string().regex(UUID_RE).optional(),
  tipoGasto: z
    .union([
      z.literal(TipoGastoAprobador.ReembolsoCajaChica),
      z.literal(TipoGastoAprobador.Viaticos),
      z.literal(TipoGastoAprobador.TarjetaCreditoEmpresarial),
      z.literal(TipoGastoAprobador.OtrosSinOc),
    ])
    .optional(),
  soloVigentes: z.boolean().optional(),
});

export type AprobadoresLimitesSearch = z.infer<
  typeof AprobadoresLimitesSearchSchema
>;

export const PoliticasViaticosSearchSchema = z.object({
  puestoId: z.string().regex(UUID_RE).optional(),
  tipoDestino: z
    .union([
      z.literal(TipoDestinoViatico.Nacional),
      z.literal(TipoDestinoViatico.Internacional),
    ])
    .optional(),
});

export type PoliticasViaticosSearch = z.infer<
  typeof PoliticasViaticosSearchSchema
>;
