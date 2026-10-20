import { describe, expect, it } from 'vitest';
import { EstadoPedidoFacturable, EstadoTimbrado } from '../api/types';
import { ESTADOS_UI, estadoUi } from './estados-ui';

describe('presentación de estados de Facturación', () => {
  it('cubre todos los estados reales de pedidos y comprobantes', () => {
    for (const estado of Object.keys(EstadoPedidoFacturable)) expect(ESTADOS_UI.pedido[estado]).toBeDefined();
    for (const entidad of ['factura', 'rep', 'cartaPorte']) {
      for (const estado of Object.keys(EstadoTimbrado)) expect(ESTADOS_UI[entidad][estado]).toBeDefined();
    }
  });
  it('mantiene tonos consistentes y hace visibles los estados desconocidos', () => {
    expect(estadoUi('factura', 'Timbrado').tono).toBe('success');
    expect(estadoUi('factura', 'TimbradoFallido').tono).toBe('danger');
    expect(estadoUi('factura', 'CancelacionPendiente').tono).toBe('neutral');
    expect(estadoUi('sesion', 'EnArqueo')).toEqual({ tono: 'warning', texto: 'En arqueo' });
    expect(estadoUi('factura', 'Otro')).toEqual({ tono: 'neutral', texto: 'Otro' });
    expect(estadoUi('anticipo', null)).toEqual({ tono: 'neutral', texto: '—' });
  });
});
