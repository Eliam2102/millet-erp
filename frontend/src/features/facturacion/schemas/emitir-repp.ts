import { z } from 'zod';

/**
 * Schema del form "Nuevo REPP" (complemento de pago, FE-F6). Multi-factura;
 * todas deben ser del mismo receptor (lo valida el backend). Las facturas se
 * eligen con <c>FacturaPpdPicker</c> (saldo por cobrar, [Decisión 13-K]).
 */
export const ReppFacturaSchema = z.object({
  facturaVentaId: z.string().uuid('Selecciona una factura'),
  importePagado: z.number().positive('El importe debe ser mayor a 0'),
});

export const EmitirReppSchema = z.object({
  sucursalId: z.string().uuid('Selecciona una sucursal'),
  fechaPago: z.string().min(1, 'Fecha de pago requerida'),
  monedaPago: z.string().length(3, 'Código ISO de 3 letras'),
  tcPago: z.number().positive().nullable(),
  formaPagoReal: z.string().min(1, 'Forma de pago').max(5),
  cuentaOrdenante: z.string().max(50).nullable(),
  cuentaBeneficiaria: z.string().max(50).nullable(),
  referenciaPago: z.string().max(100).nullable(),
  facturas: z.array(ReppFacturaSchema).min(1, 'Agrega al menos una factura'),
}).superRefine((values, ctx) => {
  // Moneda extranjera exige tipo de cambio (CFDI 4.0); sin esto el backend
  // rechaza con 400. Se valida aquí para dar error inline en el campo.
  if (
    values.monedaPago !== 'MXN' &&
    (values.tcPago == null || values.tcPago <= 0)
  ) {
    ctx.addIssue({
      code: 'custom',
      path: ['tcPago'],
      message: 'Con moneda distinta a MXN captura el tipo de cambio.',
    });
  }
});

export type EmitirReppValues = z.infer<typeof EmitirReppSchema>;
