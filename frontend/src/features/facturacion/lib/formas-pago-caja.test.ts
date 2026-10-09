import { describe, expect, it } from 'vitest';
import { formasPagoCaja, formasPagoVigentes } from './formas-pago-caja';
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
