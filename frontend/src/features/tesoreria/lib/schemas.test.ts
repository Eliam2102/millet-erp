import { describe, expect, it } from 'vitest';
import { PagosSearchSchema } from './pagos-search-schema';
import { MovimientosSearchSchema } from './movimientos-search-schema';
import { formatoFecha, formatoMonto } from './formato';

describe('PagosSearchSchema (TES-FE-PR2)', () => {
  it('acepta filtros válidos', () => {
    const r = PagosSearchSchema.parse({
      moneda: 'MXN',
      venceDesde: '2026-07-01',
      q: 'acme',
      offset: 0,
      limit: 100,
    });
    expect(r.moneda).toBe('MXN');
  });

  it('rechaza moneda que no es ISO-3', () => {
    expect(() => PagosSearchSchema.parse({ moneda: 'PESOS' })).toThrow();
  });

  it('objeto vacío es válido (todos opcionales)', () => {
    expect(PagosSearchSchema.parse({})).toEqual({});
  });
});

describe('MovimientosSearchSchema (TES-FE-PR2)', () => {
  it('acepta sentido/estados del mirror', () => {
    const r = MovimientosSearchSchema.parse({
      sentido: 2,
      estadoAplicacion: 1,
      estadoConciliacion: 2,
    });
    expect(r.sentido).toBe(2);
  });

  it('rechaza valores fuera del check constraint', () => {
    expect(() => MovimientosSearchSchema.parse({ sentido: 3 })).toThrow();
    expect(() => MovimientosSearchSchema.parse({ estadoAplicacion: 4 })).toThrow();
  });
});

describe('formato (TES-FE-PR2)', () => {
  it('formatoMonto usa es-MX con 2 decimales + moneda', () => {
    expect(formatoMonto(12345.5, 'MXN')).toBe('12,345.50 MXN');
  });

  it('formatoFecha convierte DateOnly sin timezone shift', () => {
    expect(formatoFecha('2026-07-15')).toBe('15/07/2026');
  });
});
