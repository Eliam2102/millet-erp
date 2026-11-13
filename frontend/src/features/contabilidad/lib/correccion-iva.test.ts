import { describe, expect, it } from 'vitest';
import type { DocumentoLiquidacionIva } from '../api/liquidacion-iva';
import { documentosCorregibles, motivoCorreccionValido } from './correccion-iva';

const doc = (cambios: Partial<DocumentoLiquidacionIva>): DocumentoLiquidacionIva => ({
  saldoId: 's1', lado: 'Acreditable', origenTipo: 'factura_proveedor', documentoId: 'd1', folio: 'F-1', uuid: 'UUID-FIX',
  metodoPago: 'PPD', fecha: '2026-11-05', moneda: 'MXN', base: 11_500, iva: 1_500, aplicadoMes: 0, ivaMes: 0, ivaRetenidoMes: 0,
  ivaPendiente: 1_500, saldo: 11_500, completo: true, motivos: [], ...cambios,
});

describe('M-209 · corrección del IVA contra el XML', () => {
  it('solo facturas de proveedor con CFDI, una vez por saldo', () => {
    const r = documentosCorregibles([
      doc({}),
      doc({}),
      doc({ saldoId: 's2', lado: 'Trasladado', origenTipo: 'comprobante' }),
      doc({ saldoId: 's3', uuid: null }),
    ]);
    expect(r.map((d) => d.saldoId)).toEqual(['s1']);
  });

  it('el motivo lleva de 10 a 500 caracteres', () => {
    expect(motivoCorreccionValido('corto')).toBe(false);
    expect(motivoCorreccionValido('  FIX el XML trae 1,600  ')).toBe(true);
    expect(motivoCorreccionValido('x'.repeat(501))).toBe(false);
  });
});
