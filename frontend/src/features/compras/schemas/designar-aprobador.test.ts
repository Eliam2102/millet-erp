import { describe, expect, it } from 'vitest';
import { DesignarAprobadorSchema } from '@/features/compras/schemas/designar-aprobador';
import { RolAprobador } from '@/features/compras/api/types';

const VALID_UUID = '11111111-1111-4111-8111-111111111111';
const SEED_UUID = '00000005-0001-0000-0000-000000000001';

describe('DesignarAprobadorSchema', () => {
  it('parsea con los 3 campos requeridos + motivo null', () => {
    const r = DesignarAprobadorSchema.parse({
      departamentoId: VALID_UUID,
      rol: RolAprobador.JefeDpto,
      usuarioId: VALID_UUID,
      motivo: null,
    });
    expect(r.rol).toBe(RolAprobador.JefeDpto);
  });

  it('acepta UUID seed (regex laxo)', () => {
    const r = DesignarAprobadorSchema.parse({
      departamentoId: SEED_UUID,
      rol: RolAprobador.AutorizadorN2,
      usuarioId: SEED_UUID,
    });
    expect(r.departamentoId).toBe(SEED_UUID);
  });

  it('rechaza departamentoId inválido', () => {
    expect(() =>
      DesignarAprobadorSchema.parse({
        departamentoId: 'no-es-uuid',
        rol: RolAprobador.JefeDpto,
        usuarioId: VALID_UUID,
      }),
    ).toThrow();
  });

  it('rechaza rol fuera de rango (3)', () => {
    expect(() =>
      DesignarAprobadorSchema.parse({
        departamentoId: VALID_UUID,
        rol: 3,
        usuarioId: VALID_UUID,
      }),
    ).toThrow();
  });

  it('acepta los 3 roles válidos', () => {
    for (const rol of [
      RolAprobador.JefeDpto,
      RolAprobador.JefeAlmacen,
      RolAprobador.AutorizadorN2,
    ] as const) {
      const r = DesignarAprobadorSchema.parse({
        departamentoId: VALID_UUID,
        rol,
        usuarioId: VALID_UUID,
      });
      expect(r.rol).toBe(rol);
    }
  });

  it('rechaza motivo > 500 caracteres', () => {
    expect(() =>
      DesignarAprobadorSchema.parse({
        departamentoId: VALID_UUID,
        rol: RolAprobador.JefeDpto,
        usuarioId: VALID_UUID,
        motivo: 'a'.repeat(501),
      }),
    ).toThrow();
  });

  it('acepta motivo de 500 caracteres exactos', () => {
    const r = DesignarAprobadorSchema.parse({
      departamentoId: VALID_UUID,
      rol: RolAprobador.JefeDpto,
      usuarioId: VALID_UUID,
      motivo: 'a'.repeat(500),
    });
    expect(r.motivo).toHaveLength(500);
  });
});
