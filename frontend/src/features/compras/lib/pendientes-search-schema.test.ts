import { describe, expect, it } from 'vitest';
import {
  DEFAULT_PENDIENTES_SEARCH,
  PendientesSearchSchema,
} from '@/features/compras/lib/pendientes-search-schema';

describe('PendientesSearchSchema', () => {
  it('input vacío aplica defaults: offset=0, limit=50', () => {
    const r = PendientesSearchSchema.parse({});
    expect(r).toEqual({ offset: 0, limit: 50 });
  });

  it('coerciona strings de URL a number en offset/limit', () => {
    const r = PendientesSearchSchema.parse({ offset: '20', limit: '100' });
    expect(r.offset).toBe(20);
    expect(r.limit).toBe(100);
  });

  it('rechaza limit > 200 (cap server-side)', () => {
    expect(() =>
      PendientesSearchSchema.parse({ limit: 500 }),
    ).toThrow();
  });

  it('rechaza offset negativo', () => {
    expect(() =>
      PendientesSearchSchema.parse({ offset: -1 }),
    ).toThrow();
  });

  it('descarta departamentoId vacío (string min(1))', () => {
    expect(() =>
      PendientesSearchSchema.parse({ departamentoId: '' }),
    ).toThrow();
  });

  it('descarta q vacío', () => {
    expect(() =>
      PendientesSearchSchema.parse({ q: '' }),
    ).toThrow();
  });

  it('acepta departamentoId + q válidos', () => {
    const r = PendientesSearchSchema.parse({
      departamentoId: 'd-1',
      q: 'MID2026',
    });
    expect(r.departamentoId).toBe('d-1');
    expect(r.q).toBe('MID2026');
  });

  it('DEFAULT_PENDIENTES_SEARCH coincide con los defaults parseados', () => {
    const parsed = PendientesSearchSchema.parse({});
    expect(parsed).toEqual(DEFAULT_PENDIENTES_SEARCH);
  });
});
