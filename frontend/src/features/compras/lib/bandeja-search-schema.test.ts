import { describe, expect, it } from 'vitest';
import {
  BandejaSearchSchema,
  DEFAULT_BANDEJA_SEARCH,
} from '@/features/compras/lib/bandeja-search-schema';
import { EstadoRequisicion } from '@/features/compras/api/types';

describe('BandejaSearchSchema', () => {
  it('aplica defaults a offset y limit cuando vienen vacíos', () => {
    const result = BandejaSearchSchema.parse({});
    expect(result.offset).toBe(0);
    expect(result.limit).toBe(50);
    expect(result.estado).toBeUndefined();
  });

  it('coerce numérico de strings (URL → number)', () => {
    const result = BandejaSearchSchema.parse({
      offset: '100',
      limit: '25',
    });
    expect(result.offset).toBe(100);
    expect(result.limit).toBe(25);
  });

  it('valida estado contra los valores numéricos del enum', () => {
    const result = BandejaSearchSchema.parse({
      estado: EstadoRequisicion.EnAutorizacion,
    });
    expect(result.estado).toBe(1);
  });

  it('rechaza estado fuera del enum', () => {
    expect(() => BandejaSearchSchema.parse({ estado: 99 })).toThrow();
  });

  it('respeta el tope de limit (200)', () => {
    expect(() => BandejaSearchSchema.parse({ limit: 500 })).toThrow();
  });

  it('preserva filtros de string', () => {
    const result = BandejaSearchSchema.parse({
      departamentoId: 'd-1',
      requisitanteId: 'u-1',
      q: 'MID',
    });
    expect(result.departamentoId).toBe('d-1');
    expect(result.requisitanteId).toBe('u-1');
    expect(result.q).toBe('MID');
  });

  it('DEFAULT_BANDEJA_SEARCH es un valor parseado válido', () => {
    expect(BandejaSearchSchema.parse(DEFAULT_BANDEJA_SEARCH)).toEqual(
      DEFAULT_BANDEJA_SEARCH,
    );
  });
});
