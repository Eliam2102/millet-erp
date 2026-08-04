import { z } from 'zod';
import { TipoAnticipo } from '@/features/facturacion/api/types';

/**
 * Schema del form "Nuevo anticipo" (FE-F4). El anticipo NO admite RFC
 * genérico (es nominal). Comparte los campos fiscales de la emisión
 * (receptor + emisor + pago) más tipo y monto base. El backend valida
 * FormaPago/UsoCfdi/RegimenFiscalEmisor/Moneda contra el catálogo SAT.
 */
const tipoValues = Object.values(TipoAnticipo) as [number, ...number[]];

export const AnticipoSchema = z.object({
  sucursalId: z.string().uuid('Selecciona una sucursal'),
  clienteId: z.string().uuid('ClienteId debe ser un GUID válido'),
  // Receptor (nominal)
  receptorRfc: z.string().min(12, 'RFC del receptor').max(13),
  receptorNombre: z.string().min(1, 'Nombre / razón social').max(254),
  receptorRegimenFiscal: z.string().min(1, 'Régimen fiscal del receptor').max(5),
  receptorCodigoPostal: z.string().min(5, 'Código postal').max(10),
  receptorUsoCfdi: z.string().min(1, 'Uso CFDI').max(5),
  receptorPais: z.string().min(3, 'País').max(5),
  // Emisor
  rfcEmisor: z.string().min(12, 'RFC del emisor').max(13),
  regimenFiscalEmisor: z.string().min(1, 'Régimen fiscal del emisor').max(5),
  // Pago
  metodoPago: z.string().min(1, 'Método de pago').max(5),
  formaPago: z.string().min(1, 'Forma de pago').max(5),
  moneda: z.string().length(3, 'Código ISO de 3 letras'),
  tipoCambio: z.number().positive().nullable(),
  // Anticipo
  tipoAnticipo: z
    .number()
    .refine((v) => tipoValues.includes(v), 'Selecciona el tipo de anticipo'),
  montoBase: z.number().positive('El monto del anticipo debe ser mayor a 0'),
  tasaIvaTraslado: z.number().min(0).max(1).nullable(),
  descripcion: z.string().max(1000).nullable(),
  obraNombre: z.string().max(254).nullable(),
}).superRefine((values, ctx) => {
  // Moneda extranjera exige tipo de cambio (CFDI 4.0); sin esto el backend
  // rechaza con 400. Se valida aquí para dar error inline en el campo.
  if (
    values.moneda !== 'MXN' &&
    (values.tipoCambio == null || values.tipoCambio <= 0)
  ) {
    ctx.addIssue({
      code: 'custom',
      path: ['tipoCambio'],
      message: 'Con moneda distinta a MXN captura el tipo de cambio.',
    });
  }
});

export type AnticipoValues = z.infer<typeof AnticipoSchema>;
