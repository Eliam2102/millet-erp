import { describe, expect, it } from 'vitest';
import {
  FechaEstimadaArriboSchema,
  FraccionArancelariaSchema,
  NumeroPedimentoSchema,
} from '@/features/compras/ordenes/schemas/comercio-exterior';
import { ActualizarLineaSchema } from '@/features/compras/ordenes/schemas/actualizar-linea';

/**
 * Comercio exterior de OC (supuesto Vilo, F1-COM-06). La fracción arancelaria
 * acepta 8 o 10 dígitos; el pedimento se reemplaza completo en el API.
 */

describe('FraccionArancelariaSchema', () => {
  it('acepta vacío (sin capturar) y 8 o 10 dígitos', () => {
    expect(FraccionArancelariaSchema.parse('')).toBe('');
    expect(FraccionArancelariaSchema.parse('70071999')).toBe('70071999');
    expect(FraccionArancelariaSchema.parse('7007199999')).toBe('7007199999');
  });

  it('rechaza longitudes distintas de 8 y 10, y caracteres no numéricos', () => {
    for (const valor of ['1234567', '123456789', '12345678901', '7007-1999', 'ABCDEFGH']) {
      expect(() => FraccionArancelariaSchema.parse(valor)).toThrow(
        'La fracción arancelaria debe tener 8 o 10 dígitos.',
      );
    }
  });
});

describe('FechaEstimadaArriboSchema', () => {
  it('acepta vacío o yyyy-MM-dd y rechaza otros formatos', () => {
    expect(FechaEstimadaArriboSchema.parse('')).toBe('');
    expect(FechaEstimadaArriboSchema.parse('2026-11-30')).toBe('2026-11-30');
    expect(() => FechaEstimadaArriboSchema.parse('30/11/2026')).toThrow();
  });
});

describe('NumeroPedimentoSchema', () => {
  const valido = {
    numeroPedimento: '26 47 3813 5001234',
    numeroBl: 'MAEU123456',
    fechaEstimadaArribo: '2026-11-30',
    agenteAduanalNaviera: 'Agencia Aduanal Norte',
  };

  it('parsea y recorta espacios de los cuatro campos', () => {
    const r = NumeroPedimentoSchema.parse({
      ...valido,
      numeroBl: '  MAEU123456  ',
    });
    expect(r.numeroBl).toBe('MAEU123456');
  });

  it('rechaza ETA con formato inválido y textos sobre el máximo', () => {
    expect(() =>
      NumeroPedimentoSchema.parse({ ...valido, fechaEstimadaArribo: '2026/11/30' }),
    ).toThrow();
    expect(() =>
      NumeroPedimentoSchema.parse({ ...valido, numeroPedimento: 'x'.repeat(41) }),
    ).toThrow('Máximo 40 caracteres.');
  });
});

describe('ActualizarLineaSchema — fracción arancelaria', () => {
  it('acepta fracción de 8 dígitos y la bandera de limpieza', () => {
    expect(
      ActualizarLineaSchema.parse({ fraccionArancelaria: '70071999' }).fraccionArancelaria,
    ).toBe('70071999');
    expect(
      ActualizarLineaSchema.parse({ limpiarFraccionArancelaria: true })
        .limpiarFraccionArancelaria,
    ).toBe(true);
  });

  it('rechaza una fracción de 9 dígitos', () => {
    expect(() =>
      ActualizarLineaSchema.parse({ fraccionArancelaria: '700719999' }),
    ).toThrow('La fracción arancelaria debe tener 8 o 10 dígitos.');
  });
});
