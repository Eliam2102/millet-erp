import { describe, expect, it } from 'vitest';
import { LineaSchema } from '@/features/compras/schemas/linea';

describe('LineaSchema', () => {
  const baseValida = {
    articuloId: '11111111-1111-4111-8111-111111111111',
    cantidad: 10,
    unidadMedida: 'PZA',
    precioEstimadoMonto: 100,
    precioEstimadoMoneda: 'MXN',
    // Fase E PR2.1: el CC-Máquina es obligatorio en la línea de RQ.
    centroCostoId: '0c000000-0000-0000-0000-000000000001',
  };

  it('parsea una línea válida sin opcionales', () => {
    const result = LineaSchema.parse(baseValida);
    expect(result.articuloId).toBe(baseValida.articuloId);
    expect(result.notas).toBeUndefined();
  });

  it('rechaza cantidad ≤ 0', () => {
    expect(() => LineaSchema.parse({ ...baseValida, cantidad: 0 })).toThrow();
    expect(() => LineaSchema.parse({ ...baseValida, cantidad: -1 })).toThrow();
  });

  it('rechaza línea sin centroCostoId (obligatorio, PR2.1)', () => {
    // undefined (no enviado) y '' (no elegido en el form) → ambos rechazados.
    expect(() =>
      LineaSchema.parse({ ...baseValida, centroCostoId: undefined }),
    ).toThrow();
    expect(() => LineaSchema.parse({ ...baseValida, centroCostoId: '' })).toThrow();
  });

  it('rechaza cantidad con > 5 decimales', () => {
    expect(() =>
      LineaSchema.parse({ ...baseValida, cantidad: 1.234567 }),
    ).toThrow();
  });

  it('acepta cantidad con 5 decimales (límite exacto)', () => {
    const result = LineaSchema.parse({ ...baseValida, cantidad: 1.23456 });
    expect(result.cantidad).toBe(1.23456);
  });

  it('rechaza precioEstimadoMonto ≤ 0', () => {
    expect(() =>
      LineaSchema.parse({ ...baseValida, precioEstimadoMonto: 0 }),
    ).toThrow();
  });

  it('rechaza moneda con más de 3 letras o lowercase', () => {
    expect(() =>
      LineaSchema.parse({ ...baseValida, precioEstimadoMoneda: 'mxn' }),
    ).toThrow();
    expect(() =>
      LineaSchema.parse({ ...baseValida, precioEstimadoMoneda: 'MXNX' }),
    ).toThrow();
  });

  it('rechaza unidadMedida vacía', () => {
    expect(() =>
      LineaSchema.parse({ ...baseValida, unidadMedida: '' }),
    ).toThrow();
  });

  it('acepta opcionales (cuentaContableId, notas, fechaRequerida)', () => {
    const result = LineaSchema.parse({
      ...baseValida,
      cuentaContableId: '99999999-9999-4999-8999-999999999999',
      fechaRequerida: '2026-05-15',
      notas: 'Urgente',
    });
    expect(result.cuentaContableId).toBeDefined();
    expect(result.fechaRequerida).toBe('2026-05-15');
    expect(result.notas).toBe('Urgente');
  });

  it('rechaza notas > 500 caracteres', () => {
    expect(() =>
      LineaSchema.parse({ ...baseValida, notas: 'a'.repeat(501) }),
    ).toThrow();
  });
});
