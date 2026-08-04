import { z } from 'zod';
import { CanalCobranza, ResultadoCobranza } from '@/features/cxc/api/types';

/**
 * Schema del sheet "Registrar gestión" (CXC-FE-PR4). Espejo del
 * validator + invariantes del backend (<c>SC_PROMESA_SIN_MONTO</c>,
 * <c>SC_PROMESA_SIN_FECHA</c>, <c>SC_COMPROMISO_SOBRANTE</c>): promesa
 * de pago exige monto y fecha; los demás resultados los prohíben.
 */
const canalValues = Object.values(CanalCobranza) as [number, ...number[]];
const resultadoValues = Object.values(ResultadoCobranza) as [number, ...number[]];

export const GestionCobranzaSchema = z
  .object({
    clienteId: z.string().uuid('Selecciona un cliente'),
    canal: z.number().refine((v) => canalValues.includes(v), 'Selecciona el canal'),
    resultado: z
      .number()
      .refine((v) => resultadoValues.includes(v), 'Selecciona el resultado'),
    montoComprometido: z.number().positive('El monto debe ser > 0').nullable(),
    /** "yyyy-MM-dd" (input date). */
    fechaComprometida: z.string().nullable(),
    nota: z
      .string()
      .min(1, 'La nota de la gestión es obligatoria')
      .max(2000, 'Máximo 2000 caracteres'),
  })
  .superRefine((v, ctx) => {
    if (v.resultado === ResultadoCobranza.PromesaPago) {
      if (v.montoComprometido == null) {
        ctx.addIssue({
          code: 'custom',
          path: ['montoComprometido'],
          message: 'Una promesa de pago exige el monto comprometido.',
        });
      }
      if (v.fechaComprometida == null || v.fechaComprometida === '') {
        ctx.addIssue({
          code: 'custom',
          path: ['fechaComprometida'],
          message: 'Una promesa de pago exige la fecha comprometida.',
        });
      }
    }
  });

export type GestionCobranzaValues = z.infer<typeof GestionCobranzaSchema>;
