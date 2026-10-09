import { describe, expect, it } from 'vitest';
import { montoNetoConteo } from './monto-neto-conteo';

describe('monto neto de un conteo', () => {
  it('compensa sobrantes y faltantes sin sumar absolutos', () => {
    expect(montoNetoConteo([{ variacionValorMxn: 12000 }, { variacionValorMxn: -11500 }])).toBe(500);
  });
  it('conserva el signo y trata las líneas sin captura como cero', () => {
    expect(montoNetoConteo([{ variacionValorMxn: -12000 }, { variacionValorMxn: null }])).toBe(-12000);
    expect(montoNetoConteo([])).toBe(0);
  });
  it('suma centavos sin arrastrar residuos binarios', () => {
    expect(montoNetoConteo([{ variacionValorMxn: 0.1 }, { variacionValorMxn: 0.2 }])).toBe(0.3);
  });
});
