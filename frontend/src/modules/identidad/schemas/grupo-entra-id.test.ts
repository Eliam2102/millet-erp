import { describe, expect, it } from 'vitest';
import { AsociarGrupoEntraIdSchema } from '@/modules/identidad/schemas/grupo-entra-id';

describe('AsociarGrupoEntraIdSchema', () => {
  const valid = {
    objectId: '00000000-0000-0000-0000-000000000001',
    nombre: 'Compras - Aprobadores',
  };

  it('acepta payload válido', () => {
    const parsed = AsociarGrupoEntraIdSchema.parse(valid);
    expect(parsed.objectId).toBe(valid.objectId);
    expect(parsed.nombre).toBe(valid.nombre);
  });

  it('trim-ea espacios alrededor', () => {
    const parsed = AsociarGrupoEntraIdSchema.parse({
      objectId: '   abc-123   ',
      nombre: '   Grupo   ',
    });
    expect(parsed.objectId).toBe('abc-123');
    expect(parsed.nombre).toBe('Grupo');
  });

  it('rechaza objectId vacío', () => {
    const result = AsociarGrupoEntraIdSchema.safeParse({
      ...valid,
      objectId: '',
    });
    expect(result.success).toBe(false);
  });

  it('rechaza objectId > 100 chars', () => {
    const result = AsociarGrupoEntraIdSchema.safeParse({
      ...valid,
      objectId: 'a'.repeat(101),
    });
    expect(result.success).toBe(false);
  });

  it('rechaza nombre vacío', () => {
    const result = AsociarGrupoEntraIdSchema.safeParse({
      ...valid,
      nombre: '',
    });
    expect(result.success).toBe(false);
  });

  it('rechaza nombre > 254 chars', () => {
    const result = AsociarGrupoEntraIdSchema.safeParse({
      ...valid,
      nombre: 'a'.repeat(255),
    });
    expect(result.success).toBe(false);
  });

  it('acepta nombre con caracteres especiales (acentos y guiones)', () => {
    const result = AsociarGrupoEntraIdSchema.safeParse({
      ...valid,
      nombre: 'Logística — Choferes',
    });
    expect(result.success).toBe(true);
  });
});
