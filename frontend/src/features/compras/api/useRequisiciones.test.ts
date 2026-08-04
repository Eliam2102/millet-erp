import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useRequisiciones } from '@/features/compras/api/useRequisiciones';
import {
  EstadoRequisicion,
  Clasificacion,
  Prioridad,
} from '@/features/compras/api/types';

describe('useRequisiciones', () => {
  it('200 OK: devuelve PagedResponse con items', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones', () =>
        HttpResponse.json({
          items: [
            {
              id: 'rq-1',
              folio: 'MID2026-000001',
              folioAnio: 2026,
              estado: EstadoRequisicion.Borrador,
              clasificacion: Clasificacion.Servicio,
              prioridad: Prioridad.Normal,
              sucursalId: 's-1',
              departamentoId: 'd-1',
              requisitanteId: 'u-1',
              descripcion: null,
              fechaSolicitud: '2026-05-09T10:00:00Z',
              fechaEntregaDeseada: null,
            },
          ],
          offset: 0,
          limit: 50,
          total: 1,
        }),
      ),
    );

    const { result } = renderHook(() => useRequisiciones(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.items).toHaveLength(1);
    expect(result.current.data?.items[0].folio).toBe('MID2026-000001');
    expect(result.current.data?.total).toBe(1);
  });

  it('inyecta filtros como query params (estado + departamentoId + offset)', async () => {
    let urlVisto: string | null = null;
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 });
      }),
    );

    const { result } = renderHook(
      () =>
        useRequisiciones({
          estado: EstadoRequisicion.EnAutorizacion,
          departamentoId: 'd-42',
          offset: 100,
          limit: 25,
        }),
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVisto).toContain('estado=1');
    expect(urlVisto).toContain('departamentoId=d-42');
    expect(urlVisto).toContain('offset=100');
    expect(urlVisto).toContain('limit=25');
  });

  it('omite filtros vacíos (sin departamentoId si no se pasa)', async () => {
    let urlVisto: string | null = null;
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 });
      }),
    );

    const { result } = renderHook(() => useRequisiciones(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVisto).not.toContain('departamentoId');
    expect(urlVisto).not.toContain('estado=');
  });

  it('403 PERMISO_DENEGADO: expone ApiError con status y problem', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Permiso denegado',
            status: 403,
            code: 'PERMISO_DENEGADO',
            traceId: '00-abc-01',
          },
          {
            status: 403,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useRequisiciones(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(403);
    expect(err.code).toBe('PERMISO_DENEGADO');
  });

  it('500 server error: lanza ApiError sin retry (4xx-only retry policy)', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Error', status: 500 },
          {
            status: 500,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useRequisiciones(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number };
    expect(err.status).toBe(500);
  });
});
