import { describe, expect, it } from 'vitest';
import {
  autorizacionVoucherInvalida,
  esFormaTarjeta,
  faltaVoucherTarjeta,
  faltanUltimos4,
  pideUltimos4,
  textoVoucher,
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
  it('reconoce tarjeta de crédito (04), débito (28) y servicios (29)', () => {
    expect(esFormaTarjeta('04')).toBe(true);
    expect(esFormaTarjeta('28')).toBe(true);
    expect(esFormaTarjeta('29')).toBe(true);
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

describe('86e3tfdvt · número de autorización del voucher', () => {
  it.each(['4111111111111111', '4111 1111 1111 1111', '4111-1111-1111-1111', '12', 'AB$123'])(
    'rechaza %s (no es una autorización; nunca el número de tarjeta)',
    (v) => {
      expect(autorizacionVoucherInvalida([{ formaPago: '04', referencia: v }])).toBe(true);
    },
  );
  it.each(['123456', 'AUT-77', ' A1B2C3 '])('acepta %s', (v) => {
    expect(autorizacionVoucherInvalida([{ formaPago: '28', referencia: v }])).toBe(false);
  });
  it('no valida formato fuera de tarjeta ni cuando aún está vacío', () => {
    expect(autorizacionVoucherInvalida([{ formaPago: '03', referencia: '4111111111111111' }])).toBe(false);
    expect(autorizacionVoucherInvalida([{ formaPago: '04', referencia: '' }])).toBe(false);
  });
});

describe('86e3tfdvt · últimos 4 dígitos de la tarjeta (Millet, 30-oct)', () => {
  it('los pide en crédito (04) y débito (28); no en servicios (29) ni fuera de tarjeta', () => {
    expect(pideUltimos4('04')).toBe(true);
    expect(pideUltimos4('28')).toBe(true);
    expect(pideUltimos4('29')).toBe(false);
    expect(pideUltimos4('01')).toBe(false);
  });
  it.each([null, '', '   ', '123', '12345', '12a4'])('bloquea la tarjeta con %s', (v) => {
    expect(faltanUltimos4([{ formaPago: '04', tarjetaUltimos4: v }])).toBe(true);
  });
  it('acepta 4 dígitos y no los exige a efectivo ni a la 29', () => {
    expect(faltanUltimos4([{ formaPago: '28', tarjetaUltimos4: ' 1234 ' }])).toBe(false);
    expect(faltanUltimos4([{ formaPago: '01' }, { formaPago: '29', tarjetaUltimos4: null }])).toBe(false);
  });
  it('en pago mixto revisa cada tarjeta', () => {
    expect(
      faltanUltimos4([
        { formaPago: '04', tarjetaUltimos4: '1234' },
        { formaPago: '28', tarjetaUltimos4: '' },
      ]),
    ).toBe(true);
  });
  it('muestra el voucher como autorización y «**** 1234»', () => {
    expect(textoVoucher('123456', '1234')).toBe('123456 · **** 1234');
    expect(textoVoucher('123456', null)).toBe('123456');
    expect(textoVoucher(null, null)).toBeNull();
  });
});
