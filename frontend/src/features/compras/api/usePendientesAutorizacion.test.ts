import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { usePendientesAutorizacion } from '@/features/compras/api/usePendientesAutorizacion';
import {
  EstadoRequisicion,
  Clasificacion,
  Prioridad,
} from '@/features/compras/api/types';

describe('usePendientesAutorizacion', () => {
  it('200 OK: pega al endpoint /pendientes-autorizacion', async () => {
    let urlVisto: string | null = null;

    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({
          items: [
            {
              id: 'rq-pending',
              folio: 'MID2026-000050',
              folioAnio: 2026,
              estado: EstadoRequisicion.EnAutorizacion,
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
        });
      }),
    );

    const { result } = renderHook(() => usePendientesAutorizacion(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVisto).toContain('/pendientes-autorizacion');
    expect(urlVisto).not.toContain('?');
    expect(result.current.data?.items[0].folio).toBe('MID2026-000050');
  });

  it('inyecta departamentoId + offset + limit como query params', async () => {
    let urlVisto: string | null = null;

    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json({
          items: [],
          offset: 50,
          limit: 25,
          total: 0,
        });
      }),
    );

    const { result } = renderHook(
      () =>
        usePendientesAutorizacion({
          departamentoId: 'd-42',
          offset: 50,
          limit: 25,
        }),
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVisto).toContain('departamentoId=d-42');
    expect(urlVisto).toContain('offset=50');
    expect(urlVisto).toContain('limit=25');
  });

  it('403 PERMISO_DENEGADO: expone ApiError', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Permiso denegado',
            status: 403,
            code: 'PERMISO_DENEGADO',
          },
          {
            status: 403,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => usePendientesAutorizacion(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(403);
  });
});
