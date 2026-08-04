import { describe, expect, it } from 'vitest';
import { SucursalSchema } from '@/modules/administracion/schemas/sucursal';

describe('SucursalSchema', () => {
  it('acepta clave + nombre válidos (claveAw opcional)', () => {
    const parsed = SucursalSchema.parse({ clave: 'MID', nombre: 'Mérida' });
    expect(parsed).toEqual({ clave: 'MID', nombre: 'Mérida' });
    expect(parsed.claveAw).toBeUndefined();
  });

  it('acepta y trim-ea claveAw', () => {
    const parsed = SucursalSchema.parse({
      clave: 'CON',
      nombre: 'CONKAL',
      claveAw: ' CONKAL ',
    });
    expect(parsed.claveAw).toBe('CONKAL');
  });

  it('rechaza claveAw > 40 chars', () => {
    const result = SucursalSchema.safeParse({
      clave: 'CON',
      nombre: 'CONKAL',
      claveAw: 'A'.repeat(41),
    });
    expect(result.success).toBe(false);
  });

  it('uppercase-a la clave', () => {
    const parsed = SucursalSchema.parse({ clave: 'mid', nombre: 'Mérida' });
    expect(parsed.clave).toBe('MID');
  });

  it('rechaza clave vacía', () => {
    const result = SucursalSchema.safeParse({ clave: '', nombre: 'X' });
    expect(result.success).toBe(false);
  });

  it('rechaza clave > 20 chars', () => {
    const result = SucursalSchema.safeParse({
      clave: 'A'.repeat(21),
      nombre: 'X',
    });
    expect(result.success).toBe(false);
  });

  it('rechaza nombre vacío', () => {
    const result = SucursalSchema.safeParse({ clave: 'MID', nombre: '' });
    expect(result.success).toBe(false);
  });

  it('rechaza nombre > 254 chars', () => {
    const result = SucursalSchema.safeParse({
      clave: 'MID',
      nombre: 'a'.repeat(255),
    });
    expect(result.success).toBe(false);
  });

  it('trim-ea la clave y el nombre', () => {
    const parsed = SucursalSchema.parse({
      clave: '  MID  ',
      nombre: '  Mérida  ',
    });
    expect(parsed.clave).toBe('MID');
    expect(parsed.nombre).toBe('Mérida');
  });
});
