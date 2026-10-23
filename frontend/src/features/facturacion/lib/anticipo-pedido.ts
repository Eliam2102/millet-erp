import type { AnticipoEstadoCuenta } from '@/features/facturacion/api/types';

/**
 * 86e3tfd6n: un anticipo cobrado con número de pedido de A+W queda «ligado» en cuanto la ingesta importa ese pedido
 * del mismo cliente; mientras tanto está «pendiente de llegar». Sigue disponible en ambos casos.
 */
export function etiquetaPedidoAnticipo(
  a: Pick<AnticipoEstadoCuenta, 'pedidoOrigenRef' | 'pedidoLigado'>,
): string {
  if (!a.pedidoOrigenRef) return '';
  return ` · pedido ${a.pedidoOrigenRef} ${a.pedidoLigado ? 'ligado' : 'pendiente de llegar'}`;
}
