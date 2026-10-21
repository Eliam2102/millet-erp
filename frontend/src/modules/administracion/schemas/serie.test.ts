import { describe, expect, it } from 'vitest';
import { CrearSerieSchema } from '@/modules/administracion/schemas/serie';
import {
  ReinicioPeriodo,
  TipoDocumentoSerie,
} from '@/modules/administracion/api/types';
import {
  TIPO_DOCUMENTO_LABEL,
  esTipoSerieFiscal,
  tipoSerieRequiereSucursal,
} from '@/modules/administracion/components/series-labels';

const base = {
  folioInicial: 1,
  empresaId: '00000003-0000-0000-0000-000000000001',
  sucursalId: null as string | null,
  tipoDocumento: TipoDocumentoSerie.Cfdi as number,
  prefijo: 'FAC',
  sufijo: null,
  reinicioPeriodo: ReinicioPeriodo.None as number,
};

describe('CrearSerieSchema · Venta directa (F6.1)', () => {
  it('acepta una serie CFDI global (sin sucursal)', () => {
    expect(CrearSerieSchema.safeParse(base).success).toBe(true);
  });

  it('rechaza Venta directa sin sucursal, con el error en sucursalId', () => {
    const r = CrearSerieSchema.safeParse({
      ...base,
      tipoDocumento: TipoDocumentoSerie.VentaDirecta,
      prefijo: 'VD',
    });
    expect(r.success).toBe(false);
    if (!r.success) {
      expect(r.error.issues.map((i) => i.path[0])).toEqual(['sucursalId']);
    }
  });

  it('acepta Venta directa con sucursal', () => {
    const r = CrearSerieSchema.safeParse({
      ...base,
      tipoDocumento: TipoDocumentoSerie.VentaDirecta,
      prefijo: 'VD',
      sucursalId: '33333333-3333-4333-8333-333333333333',
    });
    expect(r.success).toBe(true);
  });
});

describe('series-labels · Venta directa (F6.1)', () => {
  it('etiqueta el tipo 6 como «Venta directa»', () => {
    expect(TipoDocumentoSerie.VentaDirecta).toBe(6);
    expect(TIPO_DOCUMENTO_LABEL[TipoDocumentoSerie.VentaDirecta]).toBe('Venta directa');
  });

  it('Venta directa es fiscal (sin reinicio) igual que CFDI y exige sucursal', () => {
    expect(esTipoSerieFiscal(TipoDocumentoSerie.VentaDirecta)).toBe(true);
    expect(esTipoSerieFiscal(TipoDocumentoSerie.Cfdi)).toBe(true);
    expect(esTipoSerieFiscal(TipoDocumentoSerie.OrdenCompra)).toBe(false);
    expect(tipoSerieRequiereSucursal(TipoDocumentoSerie.VentaDirecta)).toBe(true);
    expect(tipoSerieRequiereSucursal(TipoDocumentoSerie.Cfdi)).toBe(false);
  });
});
