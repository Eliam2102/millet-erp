import { describe, expect, it } from 'vitest';
import { etiquetaPedidoAnticipo } from '@/features/facturacion/lib/anticipo-pedido';

describe('etiquetaPedidoAnticipo (86e3tfd6n)', () => {
  it('sin número de pedido no agrega nada', () => {
    expect(etiquetaPedidoAnticipo({ pedidoOrigenRef: null, pedidoLigado: null })).toBe('');
  });

  it('con pedido que aún no llega al ERP lo marca pendiente', () => {
    expect(etiquetaPedidoAnticipo({ pedidoOrigenRef: '10425068', pedidoLigado: null })).toBe(
      ' · pedido 10425068 pendiente de llegar',
    );
  });

  it('con pedido importado lo marca ligado', () => {
    expect(
      etiquetaPedidoAnticipo({
        pedidoOrigenRef: '10425068',
        pedidoLigado: { pedidoFacturableId: 'p-1', numeroPedido: '10425068', estado: 'Importado' },
      }),
    ).toBe(' · pedido 10425068 ligado');
  });
});
