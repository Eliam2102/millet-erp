// @vitest-environment node
import { describe, expect, it } from 'vitest';
import { separarSangria } from './ReporteDocument';

// O1 (QA CON-08): el PDF descarta los espacios iniciales; la sangría de los estados financieros pasa a margen izquierdo.
describe('separarSangria', () => {
  it('convierte los espacios duros iniciales en puntos de margen', () => {
    expect(separarSangria(' '.repeat(8) + 'VENTAS')).toEqual({ texto: 'VENTAS', sangria: 20 });
  });

  it('deja intacto un texto sin sangría', () => {
    expect(separarSangria('ACTIVO')).toEqual({ texto: 'ACTIVO', sangria: 0 });
  });
});
