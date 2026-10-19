import { describe, expect, it } from 'vitest';
import {
  UmbralAprobacionSchema,
  aUpsertUmbral,
  valoresIniciales,
} from '@/features/compras/schemas/umbral-aprobacion';

const DEPTO_ID = '11111111-1111-4111-8111-111111111111';

function errorDe(
  values: { umbralMenorCuantia: string; umbralMonto: string; montoMinimoDisponibilidad?: string },
  campo: 'umbralMenorCuantia' | 'umbralMonto' | 'montoMinimoDisponibilidad',
) {
  const r = UmbralAprobacionSchema.safeParse({
    modoNivel2: 'Ambos',
    montoMinimoDisponibilidad: '',
    ...values,
  });
  return r.success ? null : (r.error.issues.find((i) => i.path[0] === campo)?.message ?? null);
}

describe('UmbralAprobacionSchema', () => {
  it('acepta N1 vacío (sin umbral N1) con N2 válido', () => {
    expect(
      UmbralAprobacionSchema.safeParse({
        umbralMenorCuantia: '',
        umbralMonto: '20000',
        modoNivel2: 'Ambos',
        montoMinimoDisponibilidad: '',
      }).success,
    ).toBe(true);
  });

  it('acepta N1 estrictamente menor que N2', () => {
    expect(
      UmbralAprobacionSchema.safeParse({
        umbralMenorCuantia: '5000.50',
        umbralMonto: '20000',
        modoNivel2: 'Ambos',
        montoMinimoDisponibilidad: '',
      }).success,
    ).toBe(true);
  });

  it('rechaza N1 igual a N2 con mensaje en el campo N1', () => {
    expect(
      errorDe({ umbralMenorCuantia: '20000', umbralMonto: '20000' }, 'umbralMenorCuantia'),
    ).toBe('El umbral N1 (menor cuantía) debe ser menor que el umbral N2.');
  });

  it('rechaza N1 mayor que N2', () => {
    expect(
      errorDe({ umbralMenorCuantia: '30000', umbralMonto: '20000' }, 'umbralMenorCuantia'),
    ).toBe('El umbral N1 (menor cuantía) debe ser menor que el umbral N2.');
  });

  it('exige N2', () => {
    expect(errorDe({ umbralMenorCuantia: '', umbralMonto: '' }, 'umbralMonto')).toBe(
      'Define el umbral N2.',
    );
  });

  it('rechaza montos con signo o más de 2 decimales', () => {
    expect(errorDe({ umbralMenorCuantia: '', umbralMonto: '-5' }, 'umbralMonto')).not.toBeNull();
    expect(errorDe({ umbralMenorCuantia: '', umbralMonto: '1.234' }, 'umbralMonto')).not.toBeNull();
    expect(
      errorDe({ umbralMenorCuantia: 'abc', umbralMonto: '100' }, 'umbralMenorCuantia'),
    ).not.toBeNull();
  });

  it('aUpsertUmbral convierte N1 vacío a null y fija MXN', () => {
    expect(
      aUpsertUmbral(DEPTO_ID, {
        umbralMenorCuantia: '',
        umbralMonto: '20000',
        modoNivel2: 'Ambos',
        montoMinimoDisponibilidad: '',
      }),
    ).toEqual({
      departamentoId: DEPTO_ID,
      umbralMenorCuantia: null,
      umbralMonto: 20000,
      moneda: 'MXN',
      modoNivel2: 'Ambos',
      montoMinimoDisponibilidad: null,
    });
  });

  it('aUpsertUmbral envía el mínimo por disponibilidad como número', () => {
    expect(
      aUpsertUmbral(DEPTO_ID, {
        umbralMenorCuantia: '',
        umbralMonto: '20000',
        modoNivel2: 'SoloDisponibilidad',
        montoMinimoDisponibilidad: '1500',
      }),
    ).toMatchObject({ modoNivel2: 'SoloDisponibilidad', montoMinimoDisponibilidad: 1500 });
  });

  it('rechaza un mínimo por disponibilidad no numérico', () => {
    expect(
      errorDe(
        { umbralMenorCuantia: '', umbralMonto: '100', montoMinimoDisponibilidad: 'abc' },
        'montoMinimoDisponibilidad',
      ),
    ).not.toBeNull();
  });

  it('valoresIniciales rellena desde el umbral vigente o deja vacío', () => {
    expect(
      valoresIniciales({
        departamentoId: DEPTO_ID,
        umbralMenorCuantia: null,
        umbralMonto: 20000,
        moneda: 'MXN',
        vigenteDesde: '2026-10-15',
        modoNivel2: 'SoloMonto',
        montoMinimoDisponibilidad: 800,
      }),
    ).toEqual({
      umbralMenorCuantia: '',
      umbralMonto: '20000',
      modoNivel2: 'SoloMonto',
      montoMinimoDisponibilidad: '800',
    });
    expect(valoresIniciales(undefined)).toEqual({
      umbralMenorCuantia: '',
      umbralMonto: '',
      modoNivel2: 'Ambos',
      montoMinimoDisponibilidad: '',
    });
    expect(valoresIniciales(undefined).modoNivel2).toBe('Ambos');
  });
});
