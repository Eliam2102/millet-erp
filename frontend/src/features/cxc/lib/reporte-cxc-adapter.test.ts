import { describe, expect, it } from 'vitest';
import { adaptarReporteCxc } from '@/features/cxc/lib/reporte-cxc-adapter';
import {
  AlineacionColumnaCxc,
  TipoColumnaReporteCxc,
  type ReporteCxcBackend,
} from '@/features/cxc/api/types';

const backend: ReporteCxcBackend = {
  titulo: 'Antigüedad de saldos',
  generadoEn: '2026-07-14T12:00:00Z',
  filtrosAplicados: [
    { label: 'Fecha de corte', valor: '2026-07-14' },
    { label: 'Moneda', valor: 'MXN' },
  ],
  columnas: [
    {
      key: 'cliente',
      label: 'Cliente',
      tipo: TipoColumnaReporteCxc.Texto,
      alineacion: AlineacionColumnaCxc.Izquierda,
    },
    {
      key: 'por_vencer',
      label: 'Por vencer',
      tipo: TipoColumnaReporteCxc.Moneda,
      alineacion: AlineacionColumnaCxc.Derecha,
    },
    // Bucket dinámico configurado en backend — el adapter lo pasa tal cual.
    {
      key: 'bucket_1_15',
      label: '1-15',
      tipo: TipoColumnaReporteCxc.Moneda,
      alineacion: AlineacionColumnaCxc.Derecha,
    },
    {
      key: 'facturas',
      label: 'Facturas',
      tipo: TipoColumnaReporteCxc.Entero,
      alineacion: AlineacionColumnaCxc.Derecha,
    },
    {
      key: 'corte',
      label: 'Corte',
      tipo: TipoColumnaReporteCxc.Fecha,
      alineacion: AlineacionColumnaCxc.Centro,
    },
  ],
  filas: [
    { cliente: 'ACME SA', por_vencer: 100, bucket_1_15: 50, facturas: 2, corte: '2026-07-14' },
  ],
  totales: { por_vencer: 100, bucket_1_15: 50, nota: 'ignorar-no-numerico' },
};

describe('adaptarReporteCxc', () => {
  it('mapea columnas key/label/tipo-enum al shape canónico del shell', () => {
    const r = adaptarReporteCxc(backend)!;
    expect(r.columnas.map((c) => c.clave)).toEqual([
      'cliente',
      'por_vencer',
      'bucket_1_15',
      'facturas',
      'corte',
    ]);
    expect(r.columnas.map((c) => c.tipo)).toEqual([
      'texto',
      'moneda',
      'moneda',
      'numero',
      'fecha',
    ]);
    expect(r.columnas[1].etiqueta).toBe('Por vencer');
  });

  it('convierte filtros lista → record y filtra totales no numéricos', () => {
    const r = adaptarReporteCxc(backend)!;
    expect(r.filtrosAplicados).toEqual({
      'Fecha de corte': '2026-07-14',
      Moneda: 'MXN',
    });
    expect(r.totales).toEqual({ por_vencer: 100, bucket_1_15: 50 });
  });

  it('preserva las filas tal cual (buckets dinámicos, sin hardcodeo)', () => {
    const r = adaptarReporteCxc(backend)!;
    expect(r.filas[0].bucket_1_15).toBe(50);
  });

  it('undefined pasa transparente (estado sin ejecutar)', () => {
    expect(adaptarReporteCxc(undefined)).toBeUndefined();
  });

  it('totales null se mantiene null', () => {
    const r = adaptarReporteCxc({ ...backend, totales: null })!;
    expect(r.totales).toBeNull();
  });
});
