import { describe, expect, it } from 'vitest';
import { ActualizarNotasLineaSchema } from '@/features/compras/schemas/notas-linea';

describe('ActualizarNotasLineaSchema', () => {
  it('acepta string ≤ 500', () => {
    const result = ActualizarNotasLineaSchema.parse({ notas: 'Hola' });
    expect(result.notas).toBe('Hola');
  });

  it('acepta null (limpiar notas)', () => {
    const result = ActualizarNotasLineaSchema.parse({ notas: null });
    expect(result.notas).toBeNull();
  });

  it('rechaza > 500 caracteres', () => {
    expect(() =>
      ActualizarNotasLineaSchema.parse({ notas: 'a'.repeat(501) }),
    ).toThrow();
  });
});
