import { describe, expect, it } from 'vitest';
import type { FacturaAbiertaItem } from '@/features/cxc/api/types';
import { sugerirPorAntiguedad } from './sugerir-por-antiguedad';

function factura(uuid: string, saldo: number, vence: string, timbrado = '2026-01-01'): FacturaAbiertaItem {
  return {
    facturaCarteraId: uuid,
    uuid,
    folio: uuid.toUpperCase(),
    moneda: 'MXN',
    metodoPago: 'PPD',
    total: saldo,
    montoPagado: 0,
    montoNc: 0,
    saldo,
    fechaTimbrado: timbrado,
    fechaVencimiento: vence,
  };
}

describe('sugerirPorAntiguedad (V61: el ERP solo sugiere)', () => {
  it('llena de la más antigua a la más nueva sin exceder el monto', () => {
    const r = sugerirPorAntiguedad(
      [factura('c', 30_000, '2026-09-30'), factura('a', 20_000, '2026-08-01'), factura('b', 20_000, '2026-09-01')],
      50_000,
    );
    expect([...r.entries()]).toEqual([
      ['a', 20_000],
      ['b', 20_000],
      ['c', 10_000],
    ]);
  });

  it('a igual vencimiento desempata por fecha de timbrado y respeta centavos', () => {
    const r = sugerirPorAntiguedad(
      [factura('n', 100.5, '2026-08-01', '2026-07-02'), factura('v', 100.25, '2026-08-01', '2026-07-01')],
      150.5,
    );
    expect(r.get('v')).toBe(100.25);
    expect(r.get('n')).toBe(50.25);
  });

  it('respeta lo disponible (saldo menos propuestas abiertas) cuando viene', () => {
    const r = sugerirPorAntiguedad([{ ...factura('a', 10_000, '2026-08-01'), disponible: 4_000 }], 9_000);
    expect(r.get('a')).toBe(4_000);
  });

  it('con monto cero no selecciona nada', () => {
    expect(sugerirPorAntiguedad([factura('a', 10, '2026-08-01')], 0).size).toBe(0);
  });
});
