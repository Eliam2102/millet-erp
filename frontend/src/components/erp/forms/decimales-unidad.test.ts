import { describe, it, expect } from 'vitest';
import {
  cabeEnDecimales,
  stepParaDecimales,
  normalizarCodigoUnidad,
  DECIMALES_FALLBACK,
} from '@/components/erp/forms/decimales-unidad';

describe('cabeEnDecimales (ADR-0046 Etapa 2 PR-2c)', () => {
  it.each([
    // pieza (0): enteros válidos, fracciones inválidas
    [2, 0, true],
    [1.5, 0, false],
    // kg (3)
    [1.5, 3, true],
    [1.25, 3, true],
    [1.2505, 3, false],
    // ceros a la derecha (no se cuenta el string): 1.5 cabe en 1
    [1.5, 1, true],
    [0.001, 3, true],
    [0.0001, 3, false],
  ])('cabeEnDecimales(%s, %s) = %s', (valor, decimales, esperado) => {
    expect(cabeEnDecimales(valor, decimales)).toBe(esperado);
  });
});

describe('stepParaDecimales', () => {
  it('pieza (0) → step 1', () => expect(stepParaDecimales(0)).toBe(1));
  it('kg (3) → step 0.001', () => expect(stepParaDecimales(3)).toBeCloseTo(0.001));
  it('ml (4) → step 0.0001', () =>
    expect(stepParaDecimales(4)).toBeCloseTo(0.0001));
});

describe('normalizarCodigoUnidad', () => {
  it.each([
    ['pza.', 'PZA'],
    [' KG ', 'KG'],
    ['l', 'L'],
    ['PZA', 'PZA'],
    [null, ''],
    [undefined, ''],
  ])('normaliza %s → %s', (input, esperado) =>
    expect(normalizarCodigoUnidad(input)).toBe(esperado),
  );
});

it('DECIMALES_FALLBACK es 5 (cap legacy)', () =>
  expect(DECIMALES_FALLBACK).toBe(5));
