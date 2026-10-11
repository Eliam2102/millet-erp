import { z } from 'zod';
import { zId } from '@/lib/z-id';
export const IngresoSchema = z.object({
  cuentaBancariaId: zId(),
  sucursalId: zId(),
  monto: z.number().positive('El importe debe ser mayor a cero.'),
  fechaValor: z.string().regex(/^\d{4}-\d{2}-\d{2}$/, 'Selecciona la fecha valor.'),
  referenciaBancaria: z.string().max(120).optional(),
  conceptoId: zId().optional(),
});
