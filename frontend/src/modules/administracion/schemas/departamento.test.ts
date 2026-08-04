import { describe, expect, it } from 'vitest';
import { DepartamentoSchema } from '@/modules/administracion/schemas/departamento';

describe('DepartamentoSchema', () => {
  it('acepta clave + nombre válidos', () => {
    const parsed = DepartamentoSchema.parse({
      clave: 'COMP',
      nombre: 'Compras',
    });
    expect(parsed).toEqual({ clave: 'COMP', nombre: 'Compras' });
  });

  it('uppercase-a la clave', () => {
    const parsed = DepartamentoSchema.parse({
      clave: 'comp',
      nombre: 'Compras',
    });
    expect(parsed.clave).toBe('COMP');
  });

  it('rechaza clave vacía', () => {
    expect(
      DepartamentoSchema.safeParse({ clave: '', nombre: 'X' }).success,
    ).toBe(false);
  });

  it('rechaza clave > 20 chars', () => {
    expect(
      DepartamentoSchema.safeParse({
        clave: 'A'.repeat(21),
        nombre: 'X',
      }).success,
    ).toBe(false);
  });

  it('rechaza nombre vacío', () => {
    expect(
      DepartamentoSchema.safeParse({ clave: 'COMP', nombre: '' }).success,
    ).toBe(false);
  });

  it('rechaza nombre > 254 chars', () => {
    expect(
      DepartamentoSchema.safeParse({
        clave: 'COMP',
        nombre: 'a'.repeat(255),
      }).success,
    ).toBe(false);
  });
});
