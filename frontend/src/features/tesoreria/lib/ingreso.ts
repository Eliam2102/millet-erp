import { z } from 'zod';
import { zId } from '@/lib/z-id';
/**
 * 86e3tfdve (opción A): cuenta ordenante del cliente (CtaOrdenante del REP). Al registrar
 * el ingreso aún no se conoce la forma de pago, así que solo se valida el patrón general
 * del catálogo c_FormaPago (espejo de `SharedKernel.Domain.Bancos.CuentaPagoSat`); la forma
 * real se valida en Facturación al revisar y emitir el REP.
 */
const PATRON_GENERAL = /^(?:[0-9]{10,11}|[0-9]{15,16}|[0-9]{18}|[A-Z0-9_]{10,50})$/;
export const FORMATO_CUENTA_ORDENANTE =
  '10, 11, 15, 16 o 18 dígitos, o de 10 a 50 caracteres alfanuméricos';

/** Quita espacios y guiones de captura y pasa a mayúsculas; vacío = sin cuenta. */
export function normalizarCuentaOrdenante(cuenta: string | null | undefined): string | undefined {
  const limpia = (cuenta ?? '').replace(/[\s-]/g, '').toUpperCase();
  return limpia.length > 0 ? limpia : undefined;
}

export const IngresoSchema = z.object({
  cuentaBancariaId: zId(),
  sucursalId: zId(),
  monto: z.number().positive('El importe debe ser mayor a cero.'),
  fechaValor: z.string().regex(/^\d{4}-\d{2}-\d{2}$/, 'Selecciona la fecha valor.'),
  referenciaBancaria: z.string().max(120).optional(),
  conceptoId: zId().optional(),
  /** C7.4: «Cobro de cliente» = BeneficiarioTipo.Cliente (2); el cliente es opcional (depósito sin identificar). */
  beneficiarioTipo: z.literal(2).optional(),
  beneficiarioRef: zId().optional(),
  cuentaOrdenante: z
    .string()
    .regex(PATRON_GENERAL, `La cuenta ordenante debe tener ${FORMATO_CUENTA_ORDENANTE}.`)
    .optional(),
});
