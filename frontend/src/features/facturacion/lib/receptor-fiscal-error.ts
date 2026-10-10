import { z } from 'zod';
import { esApiError } from '@/lib/api';
import { zId } from '@/lib/z-id';

const diagnosticoSchema = z.object({
  code: z.literal('RECEPTOR_FISCAL_INVALIDO'),
  campos: z.array(z.object({ campo: z.string().min(1), motivo: z.string().min(1) })).min(1),
  clienteId: zId().nullish(),
});

export type DiagnosticoReceptorFiscal = z.infer<typeof diagnosticoSchema>;

export function parseReceptorFiscalError(error: unknown): DiagnosticoReceptorFiscal | null {
  if (!esApiError(error) || error.status !== 422) return null;
  const resultado = diagnosticoSchema.safeParse(error.problem);
  return resultado.success ? resultado.data : null;
}
