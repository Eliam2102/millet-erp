import { describe, expect, it } from 'vitest';
import { AutorizarSchema } from '@/features/compras/schemas/autorizar';
import { NivelAutorizacion } from '@/features/compras/api/types';

describe('AutorizarSchema', () => {
  it('acepta Nivel1 con notas null', () => {
    const result = AutorizarSchema.parse({
      nivel: NivelAutorizacion.Nivel1,
      notas: null,
    });
    expect(result.nivel).toBe(NivelAutorizacion.Nivel1);
    expect(result.notas).toBeNull();
  });

  it('acepta Nivel2 con notas string', () => {
    const result = AutorizarSchema.parse({
      nivel: NivelAutorizacion.Nivel2,
      notas: 'Aprobado por director',
    });
    expect(result.nivel).toBe(NivelAutorizacion.Nivel2);
    expect(result.notas).toBe('Aprobado por director');
  });

  it('acepta sin notas (nullish)', () => {
    const result = AutorizarSchema.parse({ nivel: NivelAutorizacion.Nivel1 });
    expect(result.nivel).toBe(NivelAutorizacion.Nivel1);
  });

  it('rechaza nivel inválido (3)', () => {
    expect(() =>
      AutorizarSchema.parse({ nivel: 3, notas: null }),
    ).toThrow();
  });

  it('rechaza notas > 500 caracteres', () => {
    expect(() =>
      AutorizarSchema.parse({
        nivel: NivelAutorizacion.Nivel1,
        notas: 'a'.repeat(501),
      }),
    ).toThrow();
  });

  it('acepta notas exactamente con 500 caracteres', () => {
    const result = AutorizarSchema.parse({
      nivel: NivelAutorizacion.Nivel1,
      notas: 'a'.repeat(500),
    });
    expect(result.notas).toHaveLength(500);
  });
});
