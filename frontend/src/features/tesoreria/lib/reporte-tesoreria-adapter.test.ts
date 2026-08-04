import { describe, expect, it } from 'vitest';
import { adaptarReporteTesoreria } from './reporte-tesoreria-adapter';
import type { ReporteTesoreriaBackend } from '@/features/tesoreria/api/types';

describe('adaptarReporteTesoreria (TES-FE-PR6)', () => {
  const backend: ReporteTesoreriaBackend = {
    titulo: 'Flujo de efectivo',
    generadoEn: '2026-07-15T18:00:00+00:00',
    filtrosAplicados: [
      { label: 'Del', valor: '2026-07-01' },
      { label: 'Al', valor: '2026-07-31' },
    ],
    columnas: [
      { key: 'concepto', label: 'Concepto', tipo: 1, alineacion: 1 },
      { key: 'ingresos', label: 'Ingresos', tipo: 4, alineacion: 3 },
      { key: 'fecha', label: 'Fecha', tipo: 5, alineacion: 2 },
    ],
    filas: [{ concepto: 'Cobro de cliente', ingresos: 15000, fecha: '2026-07-10' }],
    totales: { ingresos: 15000, nota: 'no-numérico' },
  };

  it('mapea columnas enum→tipo del ReporteShell y filtros lista→record', () => {
    const r = adaptarReporteTesoreria(backend)!;

    expect(r.columnas).toEqual([
      { clave: 'concepto', etiqueta: 'Concepto', tipo: 'texto' },
      { clave: 'ingresos', etiqueta: 'Ingresos', tipo: 'moneda' },
      { clave: 'fecha', etiqueta: 'Fecha', tipo: 'fecha' },
    ]);
    expect(r.filtrosAplicados).toEqual({ Del: '2026-07-01', Al: '2026-07-31' });
  });

  it('filtra totales no numéricos y preserva los numéricos', () => {
    const r = adaptarReporteTesoreria(backend)!;
    expect(r.totales).toEqual({ ingresos: 15000 });
  });

  it('undefined pasa transparente (loading)', () => {
    expect(adaptarReporteTesoreria(undefined)).toBeUndefined();
  });
});
