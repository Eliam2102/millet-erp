import { z } from 'zod';
import { zId } from '@/lib/z-id';

/** M-203: alta de traspaso entre cuentas propias (espejo de `RegistrarTraspasoValidator`). */
export const TraspasoSchema = z
  .object({
    cuentaOrigenId: zId(),
    cuentaDestinoId: zId(),
    monto: z.number().positive('El importe debe ser mayor a cero.'),
    fechaValor: z.string().regex(/^\d{4}-\d{2}-\d{2}$/, 'Selecciona la fecha valor.'),
    referencia: z.string().trim().min(1, 'La referencia es obligatoria.').max(120),
    motivo: z.string().trim().max(400).optional(),
    sucursalId: zId().optional(),
  })
  .refine((t) => t.cuentaOrigenId !== t.cuentaDestinoId, {
    path: ['cuentaDestinoId'],
    message: 'La cuenta origen y la destino deben ser distintas.',
  });
