import { describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { QueryClient } from '@tanstack/react-query';
import {
  createTestQueryClient,
  createQueryWrapper,
} from '@/test/test-query-client';

/**
 * Cliente con <c>gcTime: Infinity</c> para tests del optimistic
 * update — el client default usa <c>gcTime: 0</c> que GC la cache
 * cuando <c>onSettled.invalidateQueries</c> marca stale sin
 * observers, y eso impide leer el snapshot post-mutación.
 */
function createOptimisticTestClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: 0, gcTime: Infinity },
      mutations: { retry: false },
    },
  });
}
import {
  useActualizarLinea,
  useActualizarNotasLinea,
  useAgregarLinea,
  useEliminarLinea,
} from '@/features/compras/api/useLineas';
import { comprasKeys } from '@/features/compras/api/keys';

const RQ_ID = '11111111-1111-4111-8111-111111111111';
const LINEA_ID = '22222222-2222-4222-8222-222222222222';

const lineaValores = {
  articuloId: '33333333-3333-4333-8333-333333333333',
  cantidad: 10,
  unidadMedida: 'PZA',
  precioEstimadoMonto: 100,
  precioEstimadoMoneda: 'MXN',
  cuentaContableId: null,
  centroCostoId: null,
  proyecto: null,
  fechaRequerida: null,
  notas: null,
};

describe('useAgregarLinea', () => {
  it('201 OK: devuelve { id, posicion, ... } e invalida el detalle', async () => {
    let bodyVisto: unknown = null;
    let idemVisto: string | null = null;

    mswServer.use(
      http.post(`*/api/v1/compras/requisiciones/${RQ_ID}/lineas`, async ({ request }) => {
        bodyVisto = await request.json();
        idemVisto = request.headers.get('Idempotency-Key');
        return HttpResponse.json(
          {
            id: 'l-new',
            requisicionId: RQ_ID,
            posicion: 1,
            version: 1,
          },
          { status: 201 },
        );
      }),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useAgregarLinea(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      requisicionId: RQ_ID,
      values: lineaValores,
      idempotencyKey: 'idem-1',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data?.id).toBe('l-new');
    expect(idemVisto).toBe('idem-1');
    expect(bodyVisto).toMatchObject({ articuloId: lineaValores.articuloId });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: comprasKeys.requisicion(RQ_ID),
    });
  });
});

describe('useActualizarLinea', () => {
  it('204 OK: PATCH estructural sin idempotency, invalida detalle', async () => {
    let idemVisto: string | null = null;
    mswServer.use(
      http.patch(
        `*/api/v1/compras/requisiciones/${RQ_ID}/lineas/${LINEA_ID}`,
        ({ request }) => {
          idemVisto = request.headers.get('Idempotency-Key');
          return new HttpResponse(null, { status: 204 });
        },
      ),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useActualizarLinea(), {
      wrapper: createQueryWrapper(queryClient),
    });

    const { notas: _ignored, ...estructural } = lineaValores;
    void _ignored;

    result.current.mutate({
      requisicionId: RQ_ID,
      lineaId: LINEA_ID,
      values: estructural,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    // PATCH no usa Idempotency-Key.
    expect(idemVisto).toBeNull();
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: comprasKeys.requisicion(RQ_ID),
    });
  });
});

describe('useEliminarLinea', () => {
  it('204 OK: DELETE invalida detalle', async () => {
    mswServer.use(
      http.delete(
        `*/api/v1/compras/requisiciones/${RQ_ID}/lineas/${LINEA_ID}`,
        () => new HttpResponse(null, { status: 204 }),
      ),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useEliminarLinea(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({ requisicionId: RQ_ID, lineaId: LINEA_ID });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: comprasKeys.requisicion(RQ_ID),
    });
  });
});

describe('useActualizarNotasLinea (optimistic)', () => {
  it('204 OK: aplica optimistic update + onSettled invalida', async () => {
    mswServer.use(
      http.patch(
        `*/api/v1/compras/requisiciones/${RQ_ID}/lineas/${LINEA_ID}/notas`,
        () => new HttpResponse(null, { status: 204 }),
      ),
    );

    const queryClient = createOptimisticTestClient();
    // Pre-popular el cache con la requisición que tiene la línea con notas viejas.
    queryClient.setQueryData(comprasKeys.requisicion(RQ_ID), {
      data: {
        id: RQ_ID,
        lineas: [
          { id: LINEA_ID, posicion: 1, notas: 'Viejas' },
          { id: 'otra', posicion: 2, notas: 'No tocar' },
        ],
      },
      etag: '5',
    });

    const { result } = renderHook(() => useActualizarNotasLinea(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      requisicionId: RQ_ID,
      lineaId: LINEA_ID,
      values: { notas: 'Nuevas' },
    });

    // Antes de await: el cache YA debe tener la nota nueva (optimistic).
    await waitFor(() => {
      const cached = queryClient.getQueryData(comprasKeys.requisicion(RQ_ID)) as {
        data: { lineas: { id: string; notas: string | null }[] };
      };
      expect(cached.data.lineas.find((l) => l.id === LINEA_ID)?.notas).toBe(
        'Nuevas',
      );
      // Otras líneas intactas.
      expect(cached.data.lineas.find((l) => l.id === 'otra')?.notas).toBe(
        'No tocar',
      );
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it('error: revierte a snapshot previa', async () => {
    mswServer.use(
      http.patch(
        `*/api/v1/compras/requisiciones/${RQ_ID}/lineas/${LINEA_ID}/notas`,
        () =>
          HttpResponse.json(
            { type: 'about:blank', title: 'Server', status: 500 },
            {
              status: 500,
              headers: { 'Content-Type': 'application/problem+json' },
            },
          ),
      ),
    );

    const queryClient = createOptimisticTestClient();
    const previaCached = {
      data: {
        id: RQ_ID,
        lineas: [{ id: LINEA_ID, posicion: 1, notas: 'Originales' }],
      },
      etag: '5',
    };
    queryClient.setQueryData(comprasKeys.requisicion(RQ_ID), previaCached);

    const { result } = renderHook(() => useActualizarNotasLinea(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      requisicionId: RQ_ID,
      lineaId: LINEA_ID,
      values: { notas: 'Cambio que falla' },
    });

    await waitFor(() => expect(result.current.isError).toBe(true));

    // Tras error: el cache vuelve al snapshot previo (onError).
    const cached = queryClient.getQueryData(comprasKeys.requisicion(RQ_ID)) as {
      data: { lineas: { id: string; notas: string | null }[] };
    };
    expect(cached.data.lineas.find((l) => l.id === LINEA_ID)?.notas).toBe(
      'Originales',
    );
  });
});
