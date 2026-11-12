import { describe, expect, it } from 'vitest';
import { ACUSE_MAXIMO_BYTES, errorAcuse, pendientes } from './papel-trabajo-iva';
import type { DiferenciaFiscalIva } from '../api/papel-trabajo-iva';

const dif = (estado: DiferenciaFiscalIva['estado']): DiferenciaFiscalIva => ({
  id: estado,
  saldoIvaDocumentoId: 's',
  folio: 'F-1',
  uuid: null,
  ivaFoto: 800,
  ivaActual: 1600,
  diferencia: 800,
  causa: 'Pago o cobro aplicado después de la liquidación',
  estado,
  ajustePolizaId: null,
  detectadaEn: '2026-11-12T00:00:00Z',
  cerradaEn: null,
});

describe('papel de trabajo del IVA (M-311)', () => {
  it('el acuse pide archivo, no vacío y de máximo 5 MB', () => {
    expect(errorAcuse(null)).toMatch(/Elige/);
    expect(errorAcuse(new File([], 'a.pdf'))).toMatch(/vacío/);
    expect(errorAcuse(new File([new Uint8Array(ACUSE_MAXIMO_BYTES + 1)], 'a.pdf'))).toMatch(/5 MB/);
    expect(errorAcuse(new File(['x'], 'a.pdf'))).toBeNull();
  });

  it('solo las diferencias pendientes se listan como pendientes', () => {
    expect(pendientes([dif('Pendiente'), dif('Cerrada')]).map((d) => d.estado)).toEqual(['Pendiente']);
  });
});
