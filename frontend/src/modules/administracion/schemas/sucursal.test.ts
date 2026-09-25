import { describe, expect, it } from 'vitest';
import { SucursalSchema } from '@/modules/administracion/schemas/sucursal';
import { TipoSucursal } from '@/modules/administracion/api/types';

describe('SucursalSchema', () => {
  it('acepta clave + nombre válidos (claveAw opcional)', () => {
    const parsed = SucursalSchema.parse({
      clave: 'MID',
      nombre: 'Mérida',
      tipo: TipoSucursal.Taller,
    });
    expect(parsed).toEqual({
      clave: 'MID',
      nombre: 'Mérida',
      tipo: TipoSucursal.Taller,
    });
    expect(parsed.claveAw).toBeUndefined();
  });

  it('acepta y trim-ea claveAw', () => {
    const parsed = SucursalSchema.parse({
      clave: 'CON',
      nombre: 'CONKAL',
      tipo: TipoSucursal.Taller,
      claveAw: ' CONKAL ',
    });
    expect(parsed.claveAw).toBe('CONKAL');
  });

  it('rechaza claveAw > 40 chars', () => {
    const result = SucursalSchema.safeParse({
      clave: 'CON',
      nombre: 'CONKAL',
      tipo: TipoSucursal.Taller,
      claveAw: 'A'.repeat(41),
    });
    expect(result.success).toBe(false);
  });

  it('uppercase-a la clave', () => {
    const parsed = SucursalSchema.parse({
      clave: 'mid',
      nombre: 'Mérida',
      tipo: TipoSucursal.Taller,
    });
    expect(parsed.clave).toBe('MID');
  });

  it('rechaza clave vacía', () => {
    const result = SucursalSchema.safeParse({
      clave: '',
      nombre: 'X',
      tipo: TipoSucursal.Taller,
    });
    expect(result.success).toBe(false);
  });

  it('rechaza clave > 20 chars', () => {
    const result = SucursalSchema.safeParse({
      clave: 'A'.repeat(21),
      nombre: 'X',
      tipo: TipoSucursal.Taller,
    });
    expect(result.success).toBe(false);
  });

  it('rechaza nombre vacío', () => {
    const result = SucursalSchema.safeParse({
      clave: 'MID',
      nombre: '',
      tipo: TipoSucursal.Taller,
    });
    expect(result.success).toBe(false);
  });

  it('rechaza nombre > 254 chars', () => {
    const result = SucursalSchema.safeParse({
      clave: 'MID',
      nombre: 'a'.repeat(255),
      tipo: TipoSucursal.Taller,
    });
    expect(result.success).toBe(false);
  });

  it('trim-ea la clave y el nombre', () => {
    const parsed = SucursalSchema.parse({
      clave: '  MID  ',
      nombre: '  Mérida  ',
      tipo: TipoSucursal.Taller,
    });
    expect(parsed.clave).toBe('MID');
    expect(parsed.nombre).toBe('Mérida');
  });

  it('rechaza cuando falta el tipo', () => {
    const result = SucursalSchema.safeParse({
      clave: 'MID',
      nombre: 'Mérida',
    });
    expect(result.success).toBe(false);
  });

  it('acepta tipo Planta explícito', () => {
    const parsed = SucursalSchema.parse({
      clave: 'PLN',
      nombre: 'Planta Conkal',
      tipo: TipoSucursal.Planta,
    });
    expect(parsed.tipo).toBe(TipoSucursal.Planta);
  });

  it('rechaza string para tipo', () => {
    const result = SucursalSchema.safeParse({
      clave: 'PLN',
      nombre: 'Planta Conkal',
      tipo: '2',
    });
    expect(result.success).toBe(false);
  });

  it('rechaza tipo inválido', () => {
    const result = SucursalSchema.safeParse({
      clave: 'MID',
      nombre: 'Mérida',
      tipo: 99,
    });
    expect(result.success).toBe(false);
  });
});

