import { describe, expect, it } from 'vitest';
import type { PasivoParaCorridaResponse } from '../api/types';
import { faltaParaAutorizar, totalesSeleccion } from './corridas';

const pasivo = (id: string, proveedorId: string, bruto: number, retenciones: number, neto: number): PasivoParaCorridaResponse => ({
  facturaProveedorId: id, proveedorId, proveedorRazonSocial: null, folioProveedor: null,
  fechaVencimiento: '2026-11-15', moneda: 'MXN', bruto, retenciones, neto, cuentaValidada: true,
});

describe('faltaParaAutorizar', () => {
  const base = { estado: 'EnAutorizacion' as const, firmasRequeridas: 2, firmasRecibidas: 1, rolRequerido: 'gerente', firmas: [{ usuarioId: 'u1', roles: 'jefe-tesoreria', firmadaEn: '' }] };

  it('pide la segunda firma y el rol cuando falta', () => {
    expect(faltaParaAutorizar(base)).toBe('falta 1 firma; al menos una firma debe ser del rol «gerente»');
  });
  it('con las firmas completas pero sin el rol sigue pidiendo el rol', () => {
    expect(faltaParaAutorizar({ ...base, firmasRecibidas: 2 })).toBe('al menos una firma debe ser del rol «gerente»');
  });
  it('null cuando se cumple o ya no está en autorización', () => {
    const conGerente = { ...base, firmasRecibidas: 2, firmas: [...base.firmas, { usuarioId: 'u2', roles: 'Gerente', firmadaEn: '' }] };
    expect(faltaParaAutorizar({ ...conGerente, firmas: [{ usuarioId: 'u2', roles: 'gerente', firmadaEn: '' }] })).toBeNull();
    expect(faltaParaAutorizar({ ...base, estado: 'Autorizada' as never })).toBeNull();
  });
});

describe('totalesSeleccion', () => {
  const lista = [pasivo('a', 'p1', 1160, 106.67, 1053.33), pasivo('b', 'p1', 0.1, 0, 0.1), pasivo('c', 'p2', 0.2, 0, 0.2)];

  it('suma bruto, retenciones y neto solo de lo elegido, sin error de punto flotante', () => {
    expect(totalesSeleccion(lista, new Set(['b', 'c']))).toEqual({ bruto: 0.3, retenciones: 0, neto: 0.3, proveedores: 2 });
    expect(totalesSeleccion(lista, new Set(['a']))).toEqual({ bruto: 1160, retenciones: 106.67, neto: 1053.33, proveedores: 1 });
  });
});
