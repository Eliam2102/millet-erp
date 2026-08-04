import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useCanalesVenta } from '@/features/facturacion/api/useCatalogosFacturacion';

const BASE = '*/api/v1/facturacion/catalogos';

describe('useCanalesVenta (FAC-ING-PR3)', () => {
  it('200 OK: devuelve los canales activos { id, nombre } ordenados por id', async () => {
    mswServer.use(
      http.get(`${BASE}/canales-venta`, () =>
        HttpResponse.json([
          { id: 1, nombre: 'Tienda Cancún' },
          { id: 7, nombre: 'Proyectos y Obras' },
        ]),
      ),
    );

    const { result } = renderHook(() => useCanalesVenta(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toHaveLength(2);
    expect(result.current.data?.[0]).toEqual({ id: 1, nombre: 'Tienda Cancún' });
  });

  it('enabled: false no dispara la query', () => {
    const { result } = renderHook(
      () => useCanalesVenta({ enabled: false }),
      { wrapper: createQueryWrapper() },
    );
    expect(result.current.fetchStatus).toBe('idle');
  });

  it('500 propaga el error al caller', async () => {
    mswServer.use(
      http.get(`${BASE}/canales-venta`, () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Error interno', status: 500 },
          {
            status: 500,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useCanalesVenta(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});
