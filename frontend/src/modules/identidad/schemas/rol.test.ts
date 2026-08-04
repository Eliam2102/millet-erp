import { describe, expect, it } from 'vitest';
import {
  ActualizarRolSchema,
  CrearRolSchema,
} from '@/modules/identidad/schemas/rol';

describe('CrearRolSchema', () => {
  const valid = {
    codigo: 'admin-compras',
    nombre: 'Administrador de Compras',
    descripcion: 'Gestiona requisiciones y órdenes de compra.',
  };

  it('acepta un payload válido', () => {
    const parsed = CrearRolSchema.parse(valid);
    expect(parsed.codigo).toBe('admin-compras');
    expect(parsed.nombre).toBe('Administrador de Compras');
  });

  it('lowercase-a el código ingresado en mayúsculas', () => {
    const parsed = CrearRolSchema.parse({ ...valid, codigo: 'ADMIN-COMPRAS' });
    expect(parsed.codigo).toBe('admin-compras');
  });

  it('rechaza código que no comienza con letra (empieza con dígito)', () => {
    const result = CrearRolSchema.safeParse({ ...valid, codigo: '1admin' });
    expect(result.success).toBe(false);
    if (!result.success) {
      const flat = result.error.flatten().fieldErrors;
      expect(flat.codigo?.[0]).toMatch(/comenzar con letra|minúsculas/i);
    }
  });

  it('rechaza código que comienza con guion', () => {
    const result = CrearRolSchema.safeParse({ ...valid, codigo: '-admin' });
    expect(result.success).toBe(false);
  });

  it('rechaza código con caracteres no permitidos (subrayado)', () => {
    const result = CrearRolSchema.safeParse({
      ...valid,
      codigo: 'admin_compras',
    });
    expect(result.success).toBe(false);
  });

  it('rechaza código con espacios', () => {
    const result = CrearRolSchema.safeParse({
      ...valid,
      codigo: 'admin compras',
    });
    expect(result.success).toBe(false);
  });

  it('rechaza código vacío', () => {
    const result = CrearRolSchema.safeParse({ ...valid, codigo: '' });
    expect(result.success).toBe(false);
  });

  it('rechaza código > 64 chars', () => {
    const result = CrearRolSchema.safeParse({
      ...valid,
      codigo: 'a' + 'b'.repeat(64),
    });
    expect(result.success).toBe(false);
  });

  it('acepta código de exactamente 64 caracteres', () => {
    const codigo = 'a' + 'b'.repeat(63);
    expect(codigo).toHaveLength(64);
    const result = CrearRolSchema.safeParse({ ...valid, codigo });
    expect(result.success).toBe(true);
  });

  it('rechaza nombre vacío', () => {
    const result = CrearRolSchema.safeParse({ ...valid, nombre: '' });
    expect(result.success).toBe(false);
  });

  it('rechaza nombre > 100 chars', () => {
    const result = CrearRolSchema.safeParse({
      ...valid,
      nombre: 'a'.repeat(101),
    });
    expect(result.success).toBe(false);
  });

  it('normaliza descripción vacía a null', () => {
    const parsed = CrearRolSchema.parse({ ...valid, descripcion: '' });
    expect(parsed.descripcion).toBeNull();
  });

  it('acepta descripción null directamente', () => {
    const parsed = CrearRolSchema.parse({ ...valid, descripcion: null });
    expect(parsed.descripcion).toBeNull();
  });

  it('rechaza descripción > 500 chars', () => {
    const result = CrearRolSchema.safeParse({
      ...valid,
      descripcion: 'a'.repeat(501),
    });
    expect(result.success).toBe(false);
  });
});

describe('ActualizarRolSchema', () => {
  it('acepta payload válido sin código', () => {
    const parsed = ActualizarRolSchema.parse({
      nombre: 'Nuevo nombre',
      descripcion: null,
    });
    expect(parsed.nombre).toBe('Nuevo nombre');
  });

  it('aplica las mismas reglas de longitud que CrearRolSchema', () => {
    const result = ActualizarRolSchema.safeParse({
      nombre: '',
      descripcion: null,
    });
    expect(result.success).toBe(false);
  });

  it('rechaza nombre > 100 chars', () => {
    const result = ActualizarRolSchema.safeParse({
      nombre: 'a'.repeat(101),
      descripcion: null,
    });
    expect(result.success).toBe(false);
  });
});
