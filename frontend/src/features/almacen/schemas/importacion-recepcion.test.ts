import { describe, expect, it } from 'vitest';
import { ImportacionRecepcionSchema } from './recepcion';

describe('CA3.9 importación al recibir (datos ficticios DEMO)', () => {
  const completa = { numeroPedimento: '26 16 1234 6000001', numeroBl: 'BL-DEMO', numeroContenedor: 'CONT-DEMO', aduana: '16', clavePedimento: 'A1' };
  it.each(['numeroPedimento', 'numeroBl', 'numeroContenedor', 'aduana', 'clavePedimento'])('exige %s', (campo) => {
    expect(ImportacionRecepcionSchema.safeParse({ ...completa, [campo]: '' }).success).toBe(false);
  });
  it('admite pedimento con espacios y conserva los datos', () => {
    expect(ImportacionRecepcionSchema.parse(completa)).toMatchObject(completa);
  });
  it('rechaza pedimento inválido', () => {
    expect(ImportacionRecepcionSchema.safeParse({ ...completa, numeroPedimento: 'PED-DEMO' }).success).toBe(false);
  });
});

it.each([3, 4])('P25: modalidad %s admite guía sin contenedor', (modalidad) => {
  expect(ImportacionRecepcionSchema.safeParse({ numeroPedimento: '261612346000001', numeroBl: 'GUIA-MUESTRA', numeroContenedor: '', modalidad, aduana: '16', clavePedimento: 'AF' }).success).toBe(true);
});
