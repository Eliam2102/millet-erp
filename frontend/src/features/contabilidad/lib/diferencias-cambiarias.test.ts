import { describe, expect, it } from 'vitest';
import { motivoPeriodoInvalido } from './diferencias-cambiarias';

describe('motivoPeriodoInvalido (TES-09)', () => {
  it('acepta un periodo válido, incluido el tope de 366 días', () => {
    expect(motivoPeriodoInvalido('2026-11-01', '2026-11-07')).toBeNull();
    expect(motivoPeriodoInvalido('2026-11-07', '2026-11-07')).toBeNull();
    expect(motivoPeriodoInvalido('2025-11-07', '2026-11-08')).toBeNull();
  });

  it('pide las dos fechas', () => {
    expect(motivoPeriodoInvalido('', '2026-11-07')).toMatch(/dos fechas/);
    expect(motivoPeriodoInvalido('2026-11-01', '')).toMatch(/dos fechas/);
  });

  it('rechaza la fecha final anterior a la inicial', () => {
    expect(motivoPeriodoInvalido('2026-11-08', '2026-11-01')).toMatch(/anterior a la inicial/);
  });

  it('rechaza más de un año, igual que el servidor', () => {
    expect(motivoPeriodoInvalido('2025-01-01', '2026-11-07')).toMatch(/no puede pasar de un año/);
    expect(motivoPeriodoInvalido('2025-11-07', '2026-11-09')).toMatch(/no puede pasar de un año/);
  });
});
