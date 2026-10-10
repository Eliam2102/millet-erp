import { z } from 'zod';
import { zId } from '@/lib/z-id';

export const CancelarOcConRecepcionesSchema = z.object({
  motivoCancelacionId: zId('Selecciona un motivo.'),
  motivoCancelacionTexto: z.string().trim().min(1, 'Escribe el motivo de la solicitud.').max(500),
});
export const ResolverCancelacionOcSchema = z.object({
  confirmar: z.boolean(),
  motivo: z.string().trim().min(1, 'Escribe el motivo de tu decisión.').max(500),
});
export type CancelarOcConRecepcionesValues = z.infer<typeof CancelarOcConRecepcionesSchema>;
export type ResolverCancelacionOcValues = z.infer<typeof ResolverCancelacionOcSchema>;
