import { z } from 'zod';
import { MONEDAS_LINEA_CREDITO } from '@/features/cxc/api/types';

/**
 * Schemas de los forms de liberación (CXC-FE-PR3). Espejo de los
 * validators del backend (<c>DecidirLiberacionValidator</c> /
 * <c>CrearAutorizacionCreditoValidator</c>).
 */

export const DecidirLiberacionSchema = z.object({
  pedidoRef: z
    .string()
    .min(1, 'El folio del pedido es obligatorio')
    .max(40, 'Máximo 40 caracteres'),
  clienteId: z.string().uuid('Selecciona un cliente'),
  moneda: z.enum(MONEDAS_LINEA_CREDITO),
  montoPedido: z.number().positive('El monto del pedido debe ser > 0'),
  overrideId: z.string().uuid().nullable(),
});

export type DecidirLiberacionValues = z.infer<typeof DecidirLiberacionSchema>;

export const NuevaAutorizacionSchema = z.object({
  beneficiarioUsuarioId: z.string().uuid('Selecciona al beneficiario'),
  motivo: z
    .string()
    .min(1, 'El motivo es obligatorio')
    .max(254, 'Máximo 254 caracteres'),
  clienteOPedidoRef: z
    .string()
    .min(1, 'Indica el cliente o folio de pedido')
    .max(80, 'Máximo 80 caracteres'),
  vigenciaHoras: z
    .number()
    .int('Horas enteras')
    .min(1, 'Mínimo 1 hora')
    .max(24, 'Máximo 24 horas'),
});

export type NuevaAutorizacionValues = z.infer<typeof NuevaAutorizacionSchema>;
