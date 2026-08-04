import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  useLiquidacionCaja,
  useEstadosAnticipos,
} from '@/features/facturacion/api/useReportes';

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
