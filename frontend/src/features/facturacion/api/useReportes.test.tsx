import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  useLiquidacionCaja,
  useEstadosAnticipos,
  useComparativaAnticipos,
} from '@/features/facturacion/api/useReportes';
import { EstadoComparativaAnticipo } from '@/features/facturacion/api/types';

function reporteStub(titulo: string) {
  return {
    titulo,
    generadoEn: '2026-05-30T10:00:00Z',
    filtrosAplicados: {},
    columnas: [
      { clave: 'concepto', etiqueta: 'Concepto', tipo: 'texto' },
      { clave: 'monto', etiqueta: 'Monto', tipo: 'moneda' },
    ],
    filas: [{ concepto: 'Efectivo', monto: 500 }],
    totales: { monto: 500 },
  };
}

describe('useLiquidacionCaja', () => {
  it('manda desde/hasta y devuelve el reporte', async () => {
    let urlVista = '';
    mswServer.use(
      http.get('*/api/v1/facturacion/reportes/liquidacion-caja', ({ request }) => {
        urlVista = request.url;
        return HttpResponse.json(reporteStub('Liquidación de caja'));
      }),
    );
    const { result } = renderHook(
      () => useLiquidacionCaja({ desde: '2026-05-01', hasta: '2026-05-30' }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.filas).toHaveLength(1);
    expect(urlVista).toContain('desde=');
    expect(urlVista).toContain('hasta=');
  });

  it('no dispara sin rango de fechas', () => {
    const { result } = renderHook(
      () => useLiquidacionCaja({ desde: '', hasta: '' }),
      { wrapper: createQueryWrapper() },
    );
    expect(result.current.fetchStatus).toBe('idle');
  });
});

describe('useEstadosAnticipos', () => {
  it('devuelve el reporte', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/reportes/estados-anticipos', () =>
        HttpResponse.json(reporteStub('Estados de anticipos')),
      ),
    );
    const { result } = renderHook(() => useEstadosAnticipos({}), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.totales?.monto).toBe(500);
  });
});

describe('useComparativaAnticipos', () => {
  it('manda filtros con periodo en hora de México y estado numérico', async () => {
    let urlVista = '';
    mswServer.use(
      http.get(
        '*/api/v1/facturacion/reportes/comparativa-anticipos',
        ({ request }) => {
          urlVista = request.url;
          return HttpResponse.json(reporteStub('Comparativa de anticipos'));
        },
      ),
    );
    const { result } = renderHook(
      () =>
        useComparativaAnticipos({
          sucursalId: 'suc-1',
          clienteId: 'cli-1',
          desde: '2026-10-01',
          hasta: '2026-10-31',
          corte: '2026-10-15',
          estado: EstadoComparativaAnticipo.Parcial,
        }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    const params = new URL(urlVista).searchParams;
    expect(params.get('sucursalId')).toBe('suc-1');
    expect(params.get('clienteId')).toBe('cli-1');
    // Inicio del día en America/Mexico_City (UTC-6) → 06:00Z.
    expect(params.get('desde')).toBe('2026-10-01T06:00:00.000Z');
    // Fin del día local → 05:59:59.999Z del día siguiente.
    expect(params.get('hasta')).toBe('2026-11-01T05:59:59.999Z');
    expect(params.get('corte')).toBe('2026-10-16T05:59:59.999Z');
    expect(params.get('estado')).toBe('2');
    expect(result.current.data?.totales?.monto).toBe(500);
  });

  it('sin filtros llama la ruta sin query string', async () => {
    let urlVista = '';
    mswServer.use(
      http.get(
        '*/api/v1/facturacion/reportes/comparativa-anticipos',
        ({ request }) => {
          urlVista = request.url;
          return HttpResponse.json(reporteStub('Comparativa de anticipos'));
        },
      ),
    );
    const { result } = renderHook(() => useComparativaAnticipos({}), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(new URL(urlVista).search).toBe('');
  });
});
