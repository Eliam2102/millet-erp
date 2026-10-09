import { describe, expect, it } from 'vitest';
import { estadoCobroFactura } from './estado-cobro-factura';

describe('estado del cobro de factura', () => {
  it.each([
    ['PUE', 0, 116, 'Por cobrar'],
    ['PUE', 0, 0, 'Sin monto por cobrar'],
    ['PPD', 0, 116, 'Por cobrar'],
    ['PPD', 40, 76, 'Parcial (PPD)'],
    ['PPD', 116, 0, 'Cobrada'],
    ['PPD', 0, 0, 'Sin monto por cobrar'],
    ['desconocido', 0, 116, 'Por confirmar'],
  ])('%s, pagado %s, saldo %s → %s', (metodoPago, pagadoPorRep, totalPorCobrar, esperado) => {
    expect(estadoCobroFactura({ metodoPago, pagadoPorRep, totalPorCobrar, cobroMostrador: null })).toBe(esperado);
  });
});
