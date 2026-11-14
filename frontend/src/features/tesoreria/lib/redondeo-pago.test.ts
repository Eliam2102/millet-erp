import { describe, expect, it } from 'vitest';
import type { RedondeoPagoResponse } from '../api/types';
import {
  clasificarImporte,
  normalizarRedondeos,
  redondeoDePago,
  situacionRedondeo,
} from './redondeo-pago';

const sol = (estado: RedondeoPagoResponse['estado'], redondeo = 0.4): RedondeoPagoResponse => ({
  id: 'r1',
  facturaProveedorId: 'f1',
  redondeo,
  estado,
  motivo: 'centavos',
  solicitadoPor: 'u1',
  resueltoPor: null,
  motivoRechazo: estado === 'Rechazada' ? 'no procede' : null,
});

describe('redondeoDePago', () => {
  it('positivo si se paga menos y negativo si se paga de más', () => {
    expect(redondeoDePago(1000.4, 1000)).toBe(0.4);
    expect(redondeoDePago(1000, 1000.3)).toBe(-0.3);
    expect(redondeoDePago(0.3, 0.1)).toBe(0.2);
  });
});

describe('clasificarImporte', () => {
  it('exacto, redondeo en ambos sentidos y tope de 0.99', () => {
    expect(clasificarImporte(1000.4, 1000.4, true)).toBe('exacto');
    expect(clasificarImporte(1000.4, 1000, true)).toBe('redondeo');
    expect(clasificarImporte(1000, 1000.99, true)).toBe('redondeo');
    expect(clasificarImporte(1000, 999.01, true)).toBe('redondeo');
    expect(clasificarImporte(1000, 999, true)).toBe('exacto'); // 1.00 de diferencia: pago parcial
    expect(clasificarImporte(1000, 1001, true)).toBe('invalido');
  });
  it('sin admitir redondeo (otra moneda/interno) pagar de más es inválido y menos es parcial', () => {
    expect(clasificarImporte(1000, 1000.3, false)).toBe('invalido');
    expect(clasificarImporte(1000.4, 1000, false)).toBe('exacto');
  });
  it('importes no positivos son inválidos', () => {
    expect(clasificarImporte(1000, 0, true)).toBe('invalido');
    expect(clasificarImporte(1000, Number.NaN, true)).toBe('invalido');
  });
});

describe('situacionRedondeo', () => {
  it('sin solicitudes', () => {
    expect(situacionRedondeo([], 0.4)).toMatchObject({ estado: 'sin-solicitud', autorizado: false });
  });
  it('autorizada por el mismo monto habilita el pago; por otro monto no', () => {
    expect(situacionRedondeo([sol('Autorizada')], 0.4).autorizado).toBe(true);
    expect(situacionRedondeo([sol('Autorizada', 0.5)], 0.4).autorizado).toBe(false);
  });
  it('solicitada no habilita; la vigente manda sobre una rechazada previa', () => {
    expect(situacionRedondeo([sol('Rechazada'), sol('Solicitada')], 0.4)).toMatchObject({
      estado: 'Solicitada',
      autorizado: false,
    });
  });
  it('rechazada y consumida no habilitan', () => {
    expect(situacionRedondeo([sol('Rechazada')], 0.4)).toMatchObject({ estado: 'Rechazada', autorizado: false });
    expect(situacionRedondeo([sol('Consumida')], 0.4)).toMatchObject({ estado: 'sin-solicitud', autorizado: false });
  });
});

describe('normalizarRedondeos', () => {
  it('acepta arreglo o { items }', () => {
    expect(normalizarRedondeos([sol('Autorizada')])).toHaveLength(1);
    expect(normalizarRedondeos({ items: [sol('Autorizada')] })).toHaveLength(1);
    expect(normalizarRedondeos(null)).toEqual([]);
  });
});
