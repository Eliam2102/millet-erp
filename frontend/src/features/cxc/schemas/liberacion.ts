import { z } from 'zod';
import { zId } from '@/lib/z-id';
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
  clienteId: zId('Selecciona un cliente'),
  moneda: z.enum(MONEDAS_LINEA_CREDITO),
  montoPedido: z.number().positive('El monto del pedido debe ser > 0'),
  overrideId: zId().nullable(),
});

export type DecidirLiberacionValues = z.infer<typeof DecidirLiberacionSchema>;

export const NuevaAutorizacionSchema = z.object({
  beneficiarioUsuarioId: zId('Selecciona al beneficiario'),
  motivo: z
    .string()
    .min(1, 'El motivo es obligatorio')
    .max(254, 'Máximo 254 caracteres'),
  clienteOPedidoRef: z
    .string()
    .min(1, 'Indica el folio del pedido')
    .max(80, 'Máximo 80 caracteres'),
  vigenciaHoras: z
    .number()
    .int('Horas enteras')
    .min(1, 'Mínimo 1 hora')
    .max(24, 'Máximo 24 horas'),
});

export type NuevaAutorizacionValues = z.infer<typeof NuevaAutorizacionSchema>;

/**
 * C7.3 — autorizar una decisión pendiente (cobertura 50–99 %). Espejo de
 * <c>AutorizarLiberacionPendienteValidator</c>: motivo obligatorio (sin
 * solo espacios), máximo 254.
 */
export const AutorizarPendienteSchema = z.object({
  motivo: z
    .string()
    .trim()
    .min(1, 'El motivo es obligatorio')
    .max(254, 'Máximo 254 caracteres'),
});

export type AutorizarPendienteValues = z.infer<typeof AutorizarPendienteSchema>;

/** C7.3 — rechazar una pendiente: mismo contrato que autorizar (motivo ≤ 254). */
export const RechazarPendienteSchema = AutorizarPendienteSchema;

export type RechazarPendienteValues = z.infer<typeof RechazarPendienteSchema>;
