import { describe, expect, it } from 'vitest';
import type { ClienteLookupItem } from '@/features/facturacion/api/types';
import { erroresAnticipoExterno } from './RegistrarAnticipoExternoDialog';

const cliente = { id: 'c1', clave: 'C1', razonSocial: 'Cliente de prueba', rfc: 'AAA010101AAA' } as ClienteLookupItem;
const base = {
  cliente,
  sucursalId: 's1',
  uuid: '11111111-2222-4333-8444-555555555555',
  folio: 'A-100',
  fecha: '2026-09-01',
  total: '1160',
  saldo: '600',
};

describe('P45 · registrar anticipo de SAP', () => {
  it('con todo capturado no hay pendientes', () => {
    expect(erroresAnticipoExterno(base)).toEqual([]);
  });

  it('valida UUID, RFC del cliente y saldo vivo ≤ total', () => {
    expect(erroresAnticipoExterno({ ...base, uuid: 'no-es-uuid' })).toContain('El UUID del CFDI no es válido.');
    expect(erroresAnticipoExterno({ ...base, cliente: { ...cliente, rfc: null } })).toContain(
      'El cliente no tiene RFC en el catálogo.',
    );
    expect(erroresAnticipoExterno({ ...base, saldo: '2000' })).toContain('El saldo vivo no puede ser mayor que el total.');
    expect(erroresAnticipoExterno({ ...base, total: '1160y' })).toContain('El total debe ser un importe mayor que cero.');
  });
});
