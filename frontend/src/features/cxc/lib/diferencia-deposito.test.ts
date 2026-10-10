import { describe, expect, it } from 'vitest';
import { diferenciaDeposito } from './diferencia-deposito';

describe('D13: diferencia del depósito', () => {
  it('conserva excedente sin exigir tolerancia', () => {
    expect(diferenciaDeposito(1100, 1000, null)).toEqual({ diferencia: 100, saldoAFavor: 100, faltaTolerancia: false, esAjusteNoFiscal: false, excedeTolerancia: false });
  });
  it('no crea diferencias por aritmética de flotantes', () => {
    expect(diferenciaDeposito(100.1, 50.05 + 50.05, 50).diferencia).toBe(0);
  });
  it('el límite de tolerancia es estricto y un faltante espera configuración', () => {
    expect(diferenciaDeposito(952, 1000, 50).esAjusteNoFiscal).toBe(true);
    expect(diferenciaDeposito(950, 1000, 50).excedeTolerancia).toBe(true);
    expect(diferenciaDeposito(952, 1000, null).faltaTolerancia).toBe(true);
  });
});
