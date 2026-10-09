import { describe, it, expect } from 'vitest';
import {
  RetencionSchema,
  proponerRetenciones,
  totalRetenciones,
  alertaRetenciones,
} from './retenciones-p8';
import { backendToShellShape } from './reportes-adapter';

describe('P8: retenciones y reportes', () => {
  it('propone ISR 10 % e IVA dos terceras partes sin mezclar las bases', () => {
    const p = proponerRetenciones(1000, 'HONORARIOS', [
      { concepto: 'HONORARIOS', impuesto: '001', tasa: 0.1, activa: true },
      { concepto: 'HONORARIOS', impuesto: '002', tasa: 0.10666667, activa: true },
      { concepto: 'FLETES', impuesto: '002', tasa: 0.04, activa: true },
    ]);
    expect(totalRetenciones(p)).toBe(206.67);
    expect(alertaRetenciones(100, p)).toBe(true);
    expect(alertaRetenciones(206.67, p)).toBe(false);
  });
  it('rechaza cambios de catálogo sin motivo o con identificador inválido', () => {
    expect(
      RetencionSchema.safeParse({
        id: 'x',
        concepto: 'HONORARIOS',
        descripcion: 'Prueba',
        impuesto: '001',
        tasa: 0.1,
        fuente: 'SAT',
        activa: true,
        motivo: '',
      }).success,
    ).toBe(false);
  });
  it('no presenta un importe USD con el formato fijo MXN del shell compartido', () => {
    const r = backendToShellShape({
      titulo: 'Auxiliar',
      generadoEn: '2026-10-09',
      filtrosAplicados: [],
      columnas: [
        { key: 'moneda', label: 'Moneda', tipo: 1, alineacion: 1 },
        { key: 'total', label: 'Saldo', tipo: 4, alineacion: 3 },
      ],
      filas: [{ moneda: 'USD', total: 50 }],
      totales: null,
    });
    expect(r.columnas[1]?.tipo).toBe('numero');
    expect(r.filas[0]).toEqual({ moneda: 'USD', total: 50 });
  });
  it('conserva los totales separados para pantalla, PDF y Excel', () => {
    const r = backendToShellShape({
      titulo: 'Auxiliar',
      generadoEn: '2026-10-09',
      filtrosAplicados: [],
      columnas: [],
      filas: [{ proveedor_nombre: 'Proveedor ficticio', moneda: 'MXN', total: 100 }],
      totales: {
        por_moneda: [
          { moneda: 'MXN', total: 100 },
          { moneda: 'USD', total: 50 },
        ],
      },
    });
    expect(r.filas.slice(1)).toEqual([
      {
        proveedor_nombre: 'Total MXN',
        moneda: 'MXN',
        total: 100,
        rfc: '',
        obra: '',
        en_revision: null,
      },
      {
        proveedor_nombre: 'Total USD',
        moneda: 'USD',
        total: 50,
        rfc: '',
        obra: '',
        en_revision: null,
      },
    ]);
    expect(r.totales).toBeNull();
  });
});
