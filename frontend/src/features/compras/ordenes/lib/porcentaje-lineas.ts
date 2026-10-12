import type { LineaOrdenCompraResponse } from '@/features/compras/ordenes/api/types';

/**
 * % real de avance por cantidades: Σ(campo) ÷ Σ(cantidad) de las líneas.
 * Recepción excluye servicios (no se reciben en Almacén, GAP-9).
 * undefined si no hay líneas o nada que recibir/facturar.
 */
export function porcentajeLineas(
  lineas: readonly Pick<
    LineaOrdenCompraResponse,
    'cantidad' | 'cantidadRecibida' | 'cantidadFacturada' | 'esServicio'
  >[] | undefined,
  campo: 'cantidadRecibida' | 'cantidadFacturada',
): number | undefined {
  const base = (lineas ?? []).filter(
    (l) => !(campo === 'cantidadRecibida' && l.esServicio),
  );
  const pedido = base.reduce((a, l) => a + l.cantidad, 0);
  if (pedido <= 0) return undefined;
  const avance = base.reduce((a, l) => a + l[campo], 0);
  return Math.min(100, Math.round((avance / pedido) * 100));
}
