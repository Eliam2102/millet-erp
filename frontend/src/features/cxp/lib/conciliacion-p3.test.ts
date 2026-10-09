import { describe, expect, it } from 'vitest';
import { desgloseRetenciones, lineasParaCompensar } from './conciliacion-p3';
import { CapturarFacturaLineaSchema } from '../schemas/factura';

describe('P3: captura y detalle fiscal', () => {
  it('permite una línea sin selección en el formulario pero exige vincularla antes de capturar', () => {
    const linea = {
      articuloId: null, descripcion: 'Material ficticio P3', claveProdServ: null,
      cantidad: 4, claveUnidad: 'H87', unidad: null, precioUnitario: 20,
      importe: 80, descuento: null, lineaOcId: null,
    };
    const sinVinculo = CapturarFacturaLineaSchema.safeParse(linea);
    expect(sinVinculo.success).toBe(false);
    if (!sinVinculo.success) {
      expect(sinVinculo.error.issues[0].path).toEqual(['lineaOcId']);
      expect(sinVinculo.error.issues[0].message).toContain('Selecciona la línea de OC');
    }
    expect(CapturarFacturaLineaSchema.safeParse({
      ...linea, lineaOcId: '11111111-1111-4111-8111-111111111111',
    }).success).toBe(true);
  });
  it('separa ISR e IVA retenido y suma varias retenciones del mismo impuesto', () => {
    expect(desgloseRetenciones([
      { impuesto: '001', tasa: null, importe: 100 },
      { impuesto: '002', tasa: null, importe: 106.67 },
      { impuesto: '001', tasa: null, importe: 10 },
    ])).toEqual({ isr: 110, iva: 106.67 });
  });
  it('ofrece únicamente líneas vinculadas a OC, sin duplicar la asignación de NC', () => {
    expect(lineasParaCompensar([
      { lineaOcId: null, descripcion: 'Sin vínculo' },
      { lineaOcId: 'linea-1', descripcion: 'Vidrio' },
      { lineaOcId: 'linea-1', descripcion: 'Vidrio repetido' },
    ])).toEqual([{ id: 'linea-1', etiqueta: 'Línea 2 · Vidrio' }]);
  });
});
