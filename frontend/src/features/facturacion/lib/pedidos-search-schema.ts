import { z } from 'zod';
import { EstadoPedidoFacturable, OrigenPedido } from '@/features/facturacion/api/types';

/**
 * Search params de la bandeja de pedidos facturables
 * (<c>/facturacion/pedidos</c>). Mismo patrón que las
 * <c>*-search-schema.ts</c> de Compras/CxP: filtros + búsqueda +
 * paginación en la URL para que los links sean compartibles. La ruta
 * aplica <c>validateSearch: PedidosSearchSchema.parse</c>.
 */
export const PedidosSearchSchema = z.object({
  /** Estado del pedido (valor numérico del enum). */
  estado: z
    .union([
      z.literal(EstadoPedidoFacturable.Importado),
      z.literal(EstadoPedidoFacturable.Bloqueado),
      z.literal(EstadoPedidoFacturable.Facturado),
      z.literal(EstadoPedidoFacturable.Cancelado),
      z.literal(EstadoPedidoFacturable.Excepcion),
    ])
    .optional(),
  /** Origen del pedido (valor numérico del enum). */
  origen: z
    .union([
      z.literal(OrigenPedido.Aw),
      z.literal(OrigenPedido.PlantaPintura),
      z.literal(OrigenPedido.Manual),
    ])
    .optional(),
  /** Búsqueda libre client-side (folio / cliente). */
  q: z.string().optional(),
  // Bucket "Sin asignar" de la Capa A (solo caja.leer-todas, CAJAS-PR6).
  alcance: z.literal('sin-asignar').optional(),
  offset: z.number().int().min(0).optional(),
  limit: z.number().int().min(1).max(200).optional(),
});

export type PedidosSearch = z.infer<typeof PedidosSearchSchema>;
