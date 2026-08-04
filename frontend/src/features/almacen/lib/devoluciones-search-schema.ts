import { z } from 'zod';
import { EstadoDevolucionProveedor } from '@/features/almacen/api/types';

/**
 * Schema de los <c>search params</c> de la bandeja de Devoluciones
 * (FE-F4-PR1). Lista las devoluciones a proveedor (8.B); las
 * internas (8.A) son one-shot y aparecen en las bandejas de
 * recepciones/salidas según el tipo del movimiento.
 */
export const DevolucionesSearchSchema = z.object({
  estado: z
    .union([
      z.literal(EstadoDevolucionProveedor.Borrador),
      z.literal(EstadoDevolucionProveedor.EnAutorizacion),
      z.literal(EstadoDevolucionProveedor.Autorizada),
      z.literal(EstadoDevolucionProveedor.Registrada),
      z.literal(EstadoDevolucionProveedor.ConciliadaConNcFiscal),
      z.literal(EstadoDevolucionProveedor.Rechazada),
    ])
    .optional(),
  proveedorId: z.string().min(1).optional(),
  soloPendientesNcFiscal: z.boolean().optional(),
});

export type DevolucionesSearch = z.infer<typeof DevolucionesSearchSchema>;

export const DEFAULT_DEVOLUCIONES_SEARCH: DevolucionesSearch = {};
