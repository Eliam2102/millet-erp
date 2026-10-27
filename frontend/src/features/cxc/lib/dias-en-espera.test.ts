import { describe, expect, it } from 'vitest';
import { DIAS_ESPERA_ALERTA, diasEnEspera } from './dias-en-espera';

describe('diasEnEspera (C7.4)', () => {
  it('cuenta días naturales de la fecha valor a hoy', () => {
    expect(diasEnEspera('2026-10-20', new Date(2026, 9, 26, 15))).toBe(6);
    expect(diasEnEspera('2026-10-26', new Date(2026, 9, 26, 8))).toBe(0);
  });

  it('el umbral de aviso es 5 días', () => {
    expect(DIAS_ESPERA_ALERTA).toBe(5);
  });
});
