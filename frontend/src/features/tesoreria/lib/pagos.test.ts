import { describe, expect, it } from 'vitest';
import { esPasivoSeleccionable } from './pagos';

const p = (proveedorId: string, moneda = 'MXN', proveedorBloqueado = false) => ({ proveedorId, moneda, proveedorBloqueado });

describe('esPasivoSeleccionable', () => {
  it('el proveedor bloqueado no se puede marcar, aunque no haya nada marcado (F-TES-09)', () => {
    expect(esPasivoSeleccionable(p('a', 'MXN', true), null)).toBe(false);
    expect(esPasivoSeleccionable(p('a', 'MXN', true), p('a'))).toBe(false);
  });

  it('con algo marcado, solo el mismo proveedor y moneda', () => {
    expect(esPasivoSeleccionable(p('a'), null)).toBe(true);
    expect(esPasivoSeleccionable(p('a'), p('a'))).toBe(true);
    expect(esPasivoSeleccionable(p('b'), p('a'))).toBe(false);
    expect(esPasivoSeleccionable(p('a', 'USD'), p('a'))).toBe(false);
  });
});
