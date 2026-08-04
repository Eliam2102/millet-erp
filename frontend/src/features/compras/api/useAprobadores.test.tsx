import { describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import {
  createTestQueryClient,
  createQueryWrapper,
} from '@/test/test-query-client';
import {
  useAprobadoresVigentes,
  useAprobadoresHistorico,
  useDesignarAprobador,
  useRevocarAprobador,
} from '@/features/compras/api/useAprobadores';
import { comprasKeys } from '@/features/compras/api/keys';
import { RolAprobador } from '@/features/compras/api/types';

const DEPTO_ID = '11111111-1111-4111-8111-111111111111';
const USER_ID = '22222222-2222-4222-8222-222222222222';
const APROBADOR_ID = '33333333-3333-4333-8333-333333333333';

describe('useAprobadoresVigentes', () => {
  it('200 OK sin filtros: pega al endpoint base', async () => {
    let urlVisto: string | null = null;
    mswServer.use(
      http.get('*/api/v1/compras/aprobadores', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json([]);
      }),
    );
    const { result } = renderHook(() => useAprobadoresVigentes(), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVisto).not.toContain('?');
    expect(result.current.data).toEqual([]);
  });

  it('inyecta filtros (departamentoId + rol + usuarioId) como query params', async () => {
    let urlVisto: string | null = null;
    mswServer.use(
      http.get('*/api/v1/compras/aprobadores', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json([]);
      }),
    );

    const { result } = renderHook(
      () =>
        useAprobadoresVigentes({
          departamentoId: DEPTO_ID,
          rol: RolAprobador.JefeDpto,
          usuarioId: USER_ID,
        }),
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVisto).toContain(`departamentoId=${DEPTO_ID}`);
    expect(urlVisto).toContain('rol=0');
    expect(urlVisto).toContain(`usuarioId=${USER_ID}`);
  });

  it('devuelve la lista cuando el backend responde con array', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/aprobadores', () =>
        HttpResponse.json([
          {
            id: APROBADOR_ID,
            departamentoId: DEPTO_ID,
            rol: RolAprobador.JefeDpto,
            usuarioId: USER_ID,
            vigenteDesde: '2026-05-09T10:00:00Z',
            designadoPor: USER_ID,
            motivo: null,
          },
        ]),
      ),
    );
    const { result } = renderHook(() => useAprobadoresVigentes(), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toHaveLength(1);
    expect(result.current.data?.[0].id).toBe(APROBADOR_ID);
  });
});

describe('useAprobadoresHistorico', () => {
  it('sin filtros: NO dispara fetch (enabled=false)', () => {
    let llamadas = 0;
    mswServer.use(
      http.get('*/api/v1/compras/aprobadores/historico', () => {
        llamadas++;
        return HttpResponse.json([]);
      }),
    );
    const { result } = renderHook(() => useAprobadoresHistorico({}), {
      wrapper: createQueryWrapper(),
    });
    expect(result.current.isFetching).toBe(false);
    expect(llamadas).toBe(0);
  });

  it('con filtro: dispara fetch con query string', async () => {
    let urlVisto: string | null = null;
    mswServer.use(
      http.get('*/api/v1/compras/aprobadores/historico', ({ request }) => {
        urlVisto = request.url;
        return HttpResponse.json([]);
      }),
    );
    const { result } = renderHook(
      () => useAprobadoresHistorico({ departamentoId: DEPTO_ID }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVisto).toContain(`departamentoId=${DEPTO_ID}`);
  });

  it('422 FILTRO_OBLIGATORIO: si backend devuelve error, bubblea', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/aprobadores/historico', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Filtro requerido',
            status: 422,
            code: 'FILTRO_OBLIGATORIO',
          },
          {
            status: 422,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );
    const { result } = renderHook(
      () => useAprobadoresHistorico({ departamentoId: DEPTO_ID }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(422);
    expect(err.code).toBe('FILTRO_OBLIGATORIO');
  });
});

describe('useDesignarAprobador', () => {
  it('201 OK: manda body y idempotency key, invalida familia aprobadores', async () => {
    let bodyVisto: unknown = null;
    let idempotency: string | null = null;
    mswServer.use(
      http.post(
        '*/api/v1/compras/aprobadores',
        async ({ request }) => {
          bodyVisto = await request.json();
          idempotency = request.headers.get('Idempotency-Key');
          return HttpResponse.json(
            { id: APROBADOR_ID, vigenteDesde: '2026-05-10T10:00:00Z' },
            { status: 201 },
          );
        },
      ),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useDesignarAprobador(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      values: {
        departamentoId: DEPTO_ID,
        rol: RolAprobador.JefeDpto,
        usuarioId: USER_ID,
        motivo: 'Sustituye al jefe anterior',
      },
      idempotencyKey: 'idem-d1',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(idempotency).toBe('idem-d1');
    expect(bodyVisto).toMatchObject({
      departamentoId: DEPTO_ID,
      rol: RolAprobador.JefeDpto,
      usuarioId: USER_ID,
    });
    expect(result.current.data?.id).toBe(APROBADOR_ID);
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: comprasKeys.aprobadores(),
    });
  });

  it('404 USUARIO_NO_ENCONTRADO: bubblea ApiError', async () => {
    mswServer.use(
      http.post(
        '*/api/v1/compras/aprobadores',
        () =>
          HttpResponse.json(
            {
              type: 'about:blank',
              title: 'Usuario no encontrado',
              status: 404,
              code: 'USUARIO_NO_ENCONTRADO',
            },
            {
              status: 404,
              headers: { 'Content-Type': 'application/problem+json' },
            },
          ),
      ),
    );
    const { result } = renderHook(() => useDesignarAprobador(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      values: {
        departamentoId: DEPTO_ID,
        rol: RolAprobador.JefeDpto,
        usuarioId: USER_ID,
        motivo: null,
      },
      idempotencyKey: 'idem-d2',
    });
    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(404);
    expect(err.code).toBe('USUARIO_NO_ENCONTRADO');
  });
});

describe('useRevocarAprobador', () => {
  it('204 OK: DELETE con Idempotency-Key e invalida familia', async () => {
    let idempotency: string | null = null;
    let metodo: string | null = null;
    mswServer.use(
      http.delete(
        `*/api/v1/compras/aprobadores/${APROBADOR_ID}`,
        ({ request }) => {
          idempotency = request.headers.get('Idempotency-Key');
          metodo = request.method;
          return new HttpResponse(null, { status: 204 });
        },
      ),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useRevocarAprobador(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      aprobadorId: APROBADOR_ID,
      idempotencyKey: 'idem-r1',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(metodo).toBe('DELETE');
    expect(idempotency).toBe('idem-r1');
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: comprasKeys.aprobadores(),
    });
  });

  it('404 APROBADOR_NO_ENCONTRADO: bubblea ApiError', async () => {
    mswServer.use(
      http.delete(
        `*/api/v1/compras/aprobadores/${APROBADOR_ID}`,
        () =>
          HttpResponse.json(
            {
              type: 'about:blank',
              title: 'Aprobador no encontrado',
              status: 404,
              code: 'APROBADOR_NO_ENCONTRADO',
            },
            {
              status: 404,
              headers: { 'Content-Type': 'application/problem+json' },
            },
          ),
      ),
    );
    const { result } = renderHook(() => useRevocarAprobador(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      aprobadorId: APROBADOR_ID,
      idempotencyKey: 'idem-r2',
    });
    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(404);
    expect(err.code).toBe('APROBADOR_NO_ENCONTRADO');
  });
});
