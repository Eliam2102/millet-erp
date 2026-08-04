import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import {
  createTestQueryClient,
  createQueryWrapper,
} from '@/test/test-query-client';
import { useEtag, useRequisicion } from '@/features/compras/api/useRequisicion';
import { comprasKeys } from '@/features/compras/api/keys';
import {
  EstadoRequisicion,
  Clasificacion,
  Prioridad,
} from '@/features/compras/api/types';

function makeRequisicionResponse(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: 'rq-1',
    empresaId: 'e-1',
    folio: 'MID2026-000042',
    folioAnio: 2026,
    clasificacion: Clasificacion.Servicio,
    sucursalId: 's-1',
    departamentoId: 'd-1',
    almacenDestinoId: 'a-1',
    requisitanteId: 'u-1',
    creadorId: 'u-1',
    descripcion: 'Servicio de mantenimiento',
    prioridad: Prioridad.Normal,
    fechaSolicitud: '2026-05-09T10:00:00Z',
    fechaEntregaDeseada: null,
    proveedorSugeridoId: null,
    estado: EstadoRequisicion.Borrador,
    motivoTerminacionId: null,
    motivoTerminacionTexto: null,
    actorTerminacionId: null,
    fechaTerminacion: null,
    version: 12,
    createdAt: '2026-05-09T10:00:00Z',
    updatedAt: '2026-05-09T10:00:00Z',
    lineas: [],
    autorizaciones: [],
    ...overrides,
  };
}

describe('useRequisicion', () => {
  it('200 OK: devuelve detalle y captura ETag en query meta', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones/rq-1', () =>
        HttpResponse.json(makeRequisicionResponse(), {
          headers: { ETag: '"12"' },
        }),
      ),
    );

    const queryClient = createTestQueryClient();
    const { result } = renderHook(() => useRequisicion('rq-1'), {
      wrapper: createQueryWrapper(queryClient),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.folio).toBe('MID2026-000042');
    expect(result.current.data?.version).toBe(12);

    // El cache crudo expone { data, etag }; el select solo deja data
    // visible para los consumidores. useEtag(id) lee el etag.
    const cached = queryClient.getQueryData(comprasKeys.requisicion('rq-1')) as {
      data: unknown;
      etag?: string;
    };
    expect(cached?.etag).toBe('12');
  });

  it('useEtag(id) lee el ETag guardado en meta', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones/rq-1', () =>
        HttpResponse.json(makeRequisicionResponse(), {
          headers: { ETag: '"7"' },
        }),
      ),
    );

    const queryClient = createTestQueryClient();
    const wrapper = createQueryWrapper(queryClient);

    // Primero corre el query para popular meta.
    const { result: rqResult } = renderHook(() => useRequisicion('rq-1'), {
      wrapper,
    });
    await waitFor(() => expect(rqResult.current.isSuccess).toBe(true));

    // Ahora useEtag debe devolver el etag.
    const { result: etagResult } = renderHook(() => useEtag('rq-1'), {
      wrapper,
    });
    expect(etagResult.current).toBe('7');
  });

  it('useEtag devuelve undefined si la query no se ejecutó', () => {
    const { result } = renderHook(() => useEtag('no-cargada'), {
      wrapper: createQueryWrapper(),
    });
    expect(result.current).toBeUndefined();
  });

  it('id null inhabilita el query (no fetch)', async () => {
    let llamado = false;
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones/anything', () => {
        llamado = true;
        return HttpResponse.json({});
      }),
    );

    const { result } = renderHook(() => useRequisicion(null), {
      wrapper: createQueryWrapper(),
    });

    // Le damos un tick para que cualquier fetch potencial dispare.
    await new Promise((r) => setTimeout(r, 10));
    expect(result.current.fetchStatus).toBe('idle');
    expect(llamado).toBe(false);
  });

  it('404 RQ_NO_ENCONTRADA: lanza ApiError 404', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones/rq-404', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'No encontrada',
            status: 404,
            code: 'RQ_NO_ENCONTRADA',
          },
          {
            status: 404,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useRequisicion('rq-404'), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number };
    expect(err.status).toBe(404);
  });

  it('403 PERMISO_DENEGADO: lanza ApiError 403', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones/rq-403', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Forbidden', status: 403 },
          {
            status: 403,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useRequisicion('rq-403'), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number };
    expect(err.status).toBe(403);
  });
});
