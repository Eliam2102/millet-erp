import { z } from 'zod';
import { MONEDAS_LINEA_CREDITO } from '@/features/cxc/api/types';

/**
 * Schema de la cabecera del form "Nueva propuesta de aplicación"
 * (CXC-FE-PR6). Espejo de <c>CrearPropuestaAplicacionValidator</c>; las
 * líneas factura↔importe viven fuera del form (estado del matching) y
 * se validan en el footer (Σ + tolerancia, espejo de invariantes
 * <c>PAP_*</c> del agregado).
 */
export const PropuestaAplicacionSchema = z.object({
  clienteId: z.string().uuid('Selecciona un cliente'),
  depositoRef: z
    .string()
    .min(1, 'La referencia del depósito es obligatoria')
    .max(80, 'Máximo 80 caracteres'),
  montoDeposito: z.number().positive('El monto del depósito debe ser > 0'),
  moneda: z.enum(MONEDAS_LINEA_CREDITO),
  /** Regla 2.1 del levantamiento: sin remittance no hay propuesta. */
  remittanceRef: z
    .string()
    .min(1, 'Sin remittance del cliente no se puede proponer')
    .max(120, 'Máximo 120 caracteres'),
});

export type PropuestaAplicacionValues = z.infer<typeof PropuestaAplicacionSchema>;
