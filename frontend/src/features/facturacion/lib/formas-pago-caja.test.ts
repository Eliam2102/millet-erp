import { describe, expect, it } from 'vitest';
import {
  esFormaTarjeta,
  faltaVoucherTarjeta,
  formasPagoCaja,
  formasPagoVigentes,
} from './formas-pago-caja';
import type { FormaPagoItem } from '@/modules/catalogos/api/types';

const catalogo: FormaPagoItem[] = [
  { id: 'efectivo', claveSat: '01', descripcion: 'Efectivo', activa: true },
  { id: 'cheque', claveSat: '02', descripcion: 'Cheque nominativo', activa: false },
];

describe('CA1.10 · formas de pago de Caja', () => {
  it('oculta 02 desactivada y conserva el cobro con 01 activa', () => {
    const disponibles = formasPagoCaja(catalogo);
    expect(disponibles.map((f) => f.claveSat)).toEqual(['01']);
    expect(formasPagoVigentes(['01'], disponibles)).toBe(true);
    expect(formasPagoVigentes(['02'], disponibles)).toBe(false);
  });
  it('bloquea una selección conservada tras desactivar y permite reactivarla', () => {
    expect(formasPagoVigentes(['01', '02'], catalogo)).toBe(false);
    const reactivado = catalogo.map((f) => ({ ...f, activa: true }));
    expect(formasPagoVigentes(['01', '02'], formasPagoCaja(reactivado))).toBe(true);
  });
  it('bloquea captura sin catálogo, sin selección o con una clave ajena', () => {
    expect(formasPagoVigentes(['01'], [])).toBe(false);
    expect(formasPagoVigentes([], catalogo)).toBe(false);
    expect(formasPagoVigentes(['99'], catalogo)).toBe(false);
  });
});

describe('FIX-12 · voucher obligatorio en cobros con tarjeta', () => {
  it('reconoce tarjeta de crédito (04) y de débito (28)', () => {
    expect(esFormaTarjeta('04')).toBe(true);
    expect(esFormaTarjeta('28')).toBe(true);
    expect(esFormaTarjeta('01')).toBe(false);
    expect(esFormaTarjeta('03')).toBe(false);
  });
  it('exige referencia solo en las formas de tarjeta', () => {
    expect(faltaVoucherTarjeta([{ formaPago: '01' }, { formaPago: '03', referencia: null }])).toBe(false);
    expect(faltaVoucherTarjeta([{ formaPago: '04', referencia: null }])).toBe(true);
    expect(faltaVoucherTarjeta([{ formaPago: '28', referencia: '   ' }])).toBe(true);
    expect(faltaVoucherTarjeta([{ formaPago: '04', referencia: 'AUT-77' }])).toBe(false);
  });
});
