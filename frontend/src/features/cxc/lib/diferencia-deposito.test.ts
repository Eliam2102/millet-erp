import { describe, expect, it } from 'vitest';
import { diferenciaDeposito } from './diferencia-deposito';

describe('D13: diferencia del depósito', () => {
  it('conserva excedente sin exigir tolerancia', () => {
    expect(diferenciaDeposito(1100, 1000, null)).toEqual({ diferencia: 100, saldoAFavor: 100, esRedondeo: false, esExcedenteRedondeo: false, faltaTolerancia: false, esAjusteNoFiscal: false, excedeTolerancia: false });
  });
  it('no crea diferencias por aritmética de flotantes', () => {
    expect(diferenciaDeposito(100.1, 50.05 + 50.05, 50).diferencia).toBe(0);
  });
  it('el límite de tolerancia es estricto y un faltante espera configuración', () => {
    expect(diferenciaDeposito(952, 1000, 50).esAjusteNoFiscal).toBe(true);
    expect(diferenciaDeposito(950, 1000, 50).excedeTolerancia).toBe(true);
    expect(diferenciaDeposito(952, 1000, null).faltaTolerancia).toBe(true);
  });

  it('M-326: un faltante de hasta $0.99 es redondeo (sin tolerancia ni documentación); $1.00 ya no', () => {
    const r = diferenciaDeposito(999.6, 1000, null);
    expect(r).toMatchObject({ esRedondeo: true, faltaTolerancia: false, esAjusteNoFiscal: false, excedeTolerancia: false });
    expect(diferenciaDeposito(999.01, 1000, 50).esRedondeo).toBe(true);
    const limite = diferenciaDeposito(999, 1000, 50);
    expect(limite.esRedondeo).toBe(false);
    expect(limite.esAjusteNoFiscal).toBe(true);
    expect(diferenciaDeposito(998.8, 1000, 50).esRedondeo).toBe(false);
    expect(diferenciaDeposito(1000.4, 1000, 50).esRedondeo).toBe(false);
  });

  it('M-326: un excedente de hasta $0.99 es redondeo y no saldo a favor; $1.00 sí es saldo a favor', () => {
    expect(diferenciaDeposito(1000.4, 1000, null)).toMatchObject({ esExcedenteRedondeo: true, saldoAFavor: 0, esRedondeo: false });
    expect(diferenciaDeposito(1000.99, 1000, null)).toMatchObject({ esExcedenteRedondeo: true, saldoAFavor: 0 });
    expect(diferenciaDeposito(1001, 1000, null)).toMatchObject({ esExcedenteRedondeo: false, saldoAFavor: 1 });
  });
});
