import { describe, expect, it } from 'vitest';
import { analisisToShell } from '@/features/compras/reportes/lib/analisis-adapter';
import type { AnalisisResponse } from '@/features/compras/reportes/api/useAnalisisProveedores';

const respuesta = (columnas: { key: string; tipo: number }[], filas: Record<string, unknown>[]) =>
  ({
    reporte: {
      titulo: 'T', generadoEn: '2026-10-22T00:00:00Z', filtrosAplicados: [], totales: null, filas,
      columnas: columnas.map((c) => ({ ...c, label: c.key, alineacion: 3 })),
    },
    totalesPorMoneda: [],
  }) as unknown as AnalisisResponse;

describe('analisisToShell', () => {
  it('los importes llevan 2 decimales aunque haya columna de moneda', () => {
    const r = analisisToShell(respuesta(
      [{ key: 'moneda', tipo: 1 }, { key: 'ordenado', tipo: 4 }, { key: 'ordenes', tipo: 2 }],
      [{ moneda: 'MXN', ordenado: 1800, ordenes: 1 }],
    ));
    const col = (k: string) => r.reporte.columnas.find((c) => c.clave === k)!;
    expect(col('ordenado')).toMatchObject({ tipo: 'numero', decimales: 2 });
    expect(col('ordenes').decimales).toBeNull();
  });

  it('los porcentajes se exportan como texto, no como número', () => {
    const r = analisisToShell(respuesta(
      [{ key: 'proveedor', tipo: 1 }, { key: 'porcentajeATiempo', tipo: 3 }],
      [{ proveedor: 'A', porcentajeATiempo: 50 }, { proveedor: 'B', porcentajeATiempo: null }],
    ));
    expect(r.reporte.columnas.find((c) => c.clave === 'porcentajeATiempo')!.tipo).toBe('texto');
    expect(r.reporte.filas.map((f) => f.porcentajeATiempo)).toEqual(['50.0 %', 'n/d']);
  });
});
