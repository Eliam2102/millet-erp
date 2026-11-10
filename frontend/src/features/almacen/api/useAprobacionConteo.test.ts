import { describe, expect, it } from 'vitest';
import { faltaAreaEquipoConteo } from './useAprobacionConteo';

describe('M-450 · área y equipo del ajuste por conteo', () => {
  it('sin exigencia nunca falta nada', () => {
    expect(faltaAreaEquipoConteo('', '', false, false)).toBe(false);
  });

  it('la cuenta que exige el área se cumple con el área o con el equipo', () => {
    expect(faltaAreaEquipoConteo('', '', true, false)).toBe(true);
    expect(faltaAreaEquipoConteo('  ', '', true, false)).toBe(true);
    expect(faltaAreaEquipoConteo('A01', '', true, false)).toBe(false);
    expect(faltaAreaEquipoConteo('', 'E01', true, false)).toBe(false);
  });

  it('la cuenta que exige el equipo no se cumple solo con el área', () => {
    expect(faltaAreaEquipoConteo('A01', '', true, true)).toBe(true);
    expect(faltaAreaEquipoConteo('A01', 'E01', true, true)).toBe(false);
  });
});
