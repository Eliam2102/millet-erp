import { describe, it, expect } from 'vitest';
import {
  decimalesDeFila,
  evaluarDecimalesFila,
  filasConDecimalesInvalidos,
} from '@/components/erp/forms/decimales-filas';
import type { DecimalesUnidadLookup } from '@/components/erp/forms/useDecimalesUnidad';

const lookup: DecimalesUnidadLookup = {
  porId: (id) => (id === 'pza-id' ? 0 : id === 'kg-id' ? 3 : null),
  porCodigo: (c) => (c === 'PZA' ? 0 : c === 'KG' ? 3 : null),
};

describe('decimalesDeFila', () => {
  it('porId tiene precedencia sobre porCodigo', () =>
    expect(
      decimalesDeFila(lookup, { unidadMedidaId: 'kg-id', unidadMedida: 'PZA' }),
    ).toBe(3));
  it('cae a porCodigo cuando no hay id (heredado)', () =>
    expect(decimalesDeFila(lookup, { unidadMedida: 'PZA' })).toBe(0));
  it('null cuando no resuelve (legacy / sin match) → fallback aguas arriba', () =>
    expect(decimalesDeFila(lookup, { unidadMedida: 'CAJA' })).toBeNull());
});

describe('filasConDecimalesInvalidos', () => {
  it('marca pieza 1.5; respeta kg 1.250 y entero; omite FK null', () => {
    const malas = filasConDecimalesInvalidos(lookup, [
      { unidadMedidaId: 'pza-id', cantidad: 1.5 }, // 0 — inválida
      { unidadMedidaId: 'kg-id', cantidad: 1.25 }, // 1 — ok
      { unidadMedida: 'CAJA', cantidad: 1.5 }, // 2 — no resuelve → skip
      { unidadMedidaId: 'pza-id', cantidad: 2 }, // 3 — ok (entero)
      { unidadMedidaId: 'kg-id', cantidad: 1.2505 }, // 4 — inválida (>3)
    ]);
    expect(malas).toEqual([0, 4]);
  });

  it('lista vacía / cantidades nulas → sin inválidas', () => {
    expect(filasConDecimalesInvalidos(lookup, [])).toEqual([]);
    expect(
      filasConDecimalesInvalidos(lookup, [
        { unidadMedidaId: 'pza-id', cantidad: null },
      ]),
    ).toEqual([]);
  });
});

describe('evaluarDecimalesFila (dos estados diferenciados)', () => {
  it("'invalidos' cuando la unidad resuelve y el valor no cabe (5.8 en PZA)", () =>
    expect(
      evaluarDecimalesFila(lookup, { unidadMedida: 'PZA', cantidad: 5.8 }),
    ).toBe('invalidos'));

  it("'no-resoluble' cuando la unidad no matchea el catálogo (CAJA)", () =>
    expect(
      evaluarDecimalesFila(lookup, { unidadMedida: 'CAJA', cantidad: 5.8 }),
    ).toBe('no-resoluble'));

  it("'ok' cuando la unidad resuelve y el valor cabe (5 en PZA)", () =>
    expect(
      evaluarDecimalesFila(lookup, { unidadMedida: 'PZA', cantidad: 5 }),
    ).toBe('ok'));

  it("'ok' cuando la unidad resuelve pero aún no hay cantidad", () =>
    expect(
      evaluarDecimalesFila(lookup, { unidadMedida: 'PZA', cantidad: null }),
    ).toBe('ok'));

  it('porId tiene precedencia: KG (3 dec) admite 1.250', () =>
    expect(
      evaluarDecimalesFila(lookup, { unidadMedidaId: 'kg-id', cantidad: 1.25 }),
    ).toBe('ok'));
});

describe('UNIDAD_NO_RESOLUBLE no bloquea el submit', () => {
  it('una fila no-resoluble (aunque el valor "sobre" decimales) NO entra en los índices inválidos', () => {
    // Antes del fix: dec==null se omitía en silencio (pasaba como si fuera ok).
    // Ahora es 'no-resoluble' (aviso) y SIGUE sin bloquear — el submit procede;
    // el guard server-side decide al guardar.
    expect(
      evaluarDecimalesFila(lookup, { unidadMedida: 'CAJA', cantidad: 5.8 }),
    ).toBe('no-resoluble');
    expect(
      filasConDecimalesInvalidos(lookup, [
        { unidadMedida: 'CAJA', cantidad: 5.8 },
      ]),
    ).toEqual([]);
  });
});
