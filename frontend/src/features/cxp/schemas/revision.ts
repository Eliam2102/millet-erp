import { z } from 'zod';
import {
  EstadoFirmaFisica,
  TipoEvidencia,
} from '@/features/cxp/api/types';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const DATE_ONLY_RE = /^\d{4}-\d{2}-\d{2}$/;

export const EnviarRevisionSchema = z.object({
  motivoRevisionId: z
    .string({ error: 'Selecciona un motivo de revisión.' })
    .regex(UUID_RE, 'Motivo de revisión inválido.'),
  dependenciaRevisoraId: z
    .string({ error: 'Indica la dependencia revisora.' })
    .regex(UUID_RE, 'UUID de dependencia inválido.'),
});

export type EnviarRevisionValues = z.infer<typeof EnviarRevisionSchema>;

export const LiberarRevisionSchema = z.object({
  accionTomada: z
    .string({ error: 'Describe la acción tomada.' })
    .trim()
    .min(5, 'Mínimo 5 caracteres.')
    .max(2000, 'Máximo 2000 caracteres.'),
});

export type LiberarRevisionValues = z.infer<typeof LiberarRevisionSchema>;

export const AdjuntarEvidenciaSchema = z
  .object({
    tipo: z.union([
      z.literal(TipoEvidencia.CapturaWhatsapp),
      z.literal(TipoEvidencia.Audio),
      z.literal(TipoEvidencia.Email),
      z.literal(TipoEvidencia.FirmaEscaneada),
      z.literal(TipoEvidencia.Otro),
    ]),
    comentario: z
      .string({ error: 'El comentario es obligatorio.' })
      .trim()
      .min(5, 'Mínimo 5 caracteres.')
      .max(500, 'Máximo 500 caracteres.'),
    estadoFirmaFisica: z.union([
      z.literal(EstadoFirmaFisica.NoAplica),
      z.literal(EstadoFirmaFisica.Pendiente),
      z.literal(EstadoFirmaFisica.Recibida),
    ]),
    fechaLimiteFirmaFisica: z
      .string()
      .regex(DATE_ONLY_RE, 'Formato YYYY-MM-DD.')
      .nullable(),
  })
  .refine(
    (v) =>
      v.estadoFirmaFisica !== EstadoFirmaFisica.Pendiente ||
      v.fechaLimiteFirmaFisica != null,
    {
      message:
        'Si la firma física está Pendiente debes indicar fecha límite.',
      path: ['fechaLimiteFirmaFisica'],
    },
  );

export type AdjuntarEvidenciaValues = z.infer<typeof AdjuntarEvidenciaSchema>;
