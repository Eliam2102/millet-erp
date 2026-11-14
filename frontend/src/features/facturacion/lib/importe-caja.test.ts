import { describe, expect, it } from 'vitest';
import { leerImporte, limpiarImporte } from './importe-caja';

describe('P54 · importes de caja solo numéricos', () => {
  it('quita letras y deja un punto con dos decimales', () => {
    expect(limpiarImporte('3000y')).toBe('3000');
    expect(limpiarImporte('1,500.505')).toBe('1.50');
    expect(limpiarImporte('12.3.4')).toBe('12.34');
    expect(limpiarImporte('-50')).toBe('50');
  });

  it('solo lee importes válidos', () => {
    expect(leerImporte('3000y')).toBeNull();
    expect(leerImporte('')).toBeNull();
    expect(leerImporte('500.5')).toBe(500.5);
    expect(leerImporte('0')).toBe(0);
  });
});
