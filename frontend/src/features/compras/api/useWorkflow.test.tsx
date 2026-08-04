import { describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import {
  createTestQueryClient,
  createQueryWrapper,
} from '@/test/test-query-client';
import {
  useTransmitirRequisicion,
  useAutorizarRequisicion,
  useRechazarRequisicion,
  useEliminarRequisicion,
  useCancelarRequisicion,
} from '@/features/compras/api/useWorkflow';
import { NivelAutorizacion } from '@/features/compras/api/types';
import { comprasKeys } from '@/features/compras/api/keys';

const RQ_ID = 'rq-uuid-1';
const MOTIVO_ID = '11111111-1111-4111-8111-111111111111';

describe('useTransmitirRequisicion', () => {
  it('204 OK: invalida familia compras y manda Idempotency-Key', async () => {
    let idempotencyVisto: string | null = null;

    mswServer.use(
      http.post(
        `*/api/v1/compras/requisiciones/${RQ_ID}/transmitir`,
        ({ request }) => {
          idempotencyVisto = request.headers.get('Idempotency-Key');
          return new HttpResponse(null, { status: 204 });
        },
      ),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useTransmitirRequisicion(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({ requisicionId: RQ_ID, idempotencyKey: 'idem-t1' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(idempotencyVisto).toBe('idem-t1');
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: comprasKeys.all });
  });

  it('409 CONCURRENCIA_DETECTADA: bubblea ApiError', async () => {
    mswServer.use(
      http.post(
        `*/api/v1/compras/requisiciones/${RQ_ID}/transmitir`,
        () =>
          HttpResponse.json(
            {
              type: 'about:blank',
              title: 'Conflicto',
              status: 409,
              code: 'CONCURRENCIA_DETECTADA',
            },
            {
              status: 409,
              headers: { 'Content-Type': 'application/problem+json' },
            },
          ),
      ),
    );

    const { result } = renderHook(() => useTransmitirRequisicion(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({ requisicionId: RQ_ID, idempotencyKey: 'idem-t2' });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(409);
    expect(err.code).toBe('CONCURRENCIA_DETECTADA');
  });
});

describe('useAutorizarRequisicion', () => {
  it('204 OK: manda body { nivel, notas } e invalida familia', async () => {
    let bodyVisto: unknown = null;

    mswServer.use(
      http.post(
        `*/api/v1/compras/requisiciones/${RQ_ID}/autorizaciones`,
        async ({ request }) => {
          bodyVisto = await request.json();
          return new HttpResponse(null, { status: 204 });
        },
      ),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useAutorizarRequisicion(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      requisicionId: RQ_ID,
      values: { nivel: NivelAutorizacion.Nivel1, notas: null },
      idempotencyKey: 'idem-a1',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(bodyVisto).toMatchObject({
      nivel: NivelAutorizacion.Nivel1,
      notas: null,
    });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: comprasKeys.all });
  });

  it('403 NIVEL_NO_PERMITIDO: bubblea ApiError', async () => {
    mswServer.use(
      http.post(
        `*/api/v1/compras/requisiciones/${RQ_ID}/autorizaciones`,
        () =>
          HttpResponse.json(
            {
              type: 'about:blank',
              title: 'Sin permiso',
              status: 403,
              code: 'NIVEL_NO_PERMITIDO',
            },
            {
              status: 403,
              headers: { 'Content-Type': 'application/problem+json' },
            },
          ),
      ),
    );

    const { result } = renderHook(() => useAutorizarRequisicion(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      requisicionId: RQ_ID,
      values: { nivel: NivelAutorizacion.Nivel2, notas: null },
      idempotencyKey: 'idem-a2',
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(403);
    expect(err.code).toBe('NIVEL_NO_PERMITIDO');
  });
});

describe('useRechazarRequisicion', () => {
  it('204 OK: manda body { motivoId, motivoTexto } e invalida familia', async () => {
    let bodyVisto: unknown = null;

    mswServer.use(
      http.post(
        `*/api/v1/compras/requisiciones/${RQ_ID}/rechazar`,
        async ({ request }) => {
          bodyVisto = await request.json();
          return new HttpResponse(null, { status: 204 });
        },
      ),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useRechazarRequisicion(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      requisicionId: RQ_ID,
      values: { motivoId: MOTIVO_ID, motivoTexto: 'Falta presupuesto' },
      idempotencyKey: 'idem-r1',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(bodyVisto).toMatchObject({
      motivoId: MOTIVO_ID,
      motivoTexto: 'Falta presupuesto',
    });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: comprasKeys.all });
  });

  it('422 MOTIVO_REQUIERE_TEXTO: bubblea ApiError', async () => {
    mswServer.use(
      http.post(
        `*/api/v1/compras/requisiciones/${RQ_ID}/rechazar`,
        () =>
          HttpResponse.json(
            {
              type: 'about:blank',
              title: 'Validación',
              status: 422,
              code: 'MOTIVO_REQUIERE_TEXTO',
              errores: [
                {
                  campo: 'motivoTexto',
                  codigo: 'REQUIRED',
                  mensaje: 'Detalle requerido',
                },
              ],
            },
            {
              status: 422,
              headers: { 'Content-Type': 'application/problem+json' },
            },
          ),
      ),
    );

    const { result } = renderHook(() => useRechazarRequisicion(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      requisicionId: RQ_ID,
      values: { motivoId: MOTIVO_ID, motivoTexto: null },
      idempotencyKey: 'idem-r2',
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(422);
    expect(err.code).toBe('MOTIVO_REQUIERE_TEXTO');
  });
});

describe('useEliminarRequisicion', () => {
  it('204 OK: NO manda Idempotency-Key (doc 05 §7.6) e invalida familia', async () => {
    let idempotencyVisto: string | null = 'inicial';
    let bodyVisto: unknown = null;

    mswServer.use(
      http.post(
        `*/api/v1/compras/requisiciones/${RQ_ID}/eliminar`,
        async ({ request }) => {
          idempotencyVisto = request.headers.get('Idempotency-Key');
          bodyVisto = await request.json();
          return new HttpResponse(null, { status: 204 });
        },
      ),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useEliminarRequisicion(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      requisicionId: RQ_ID,
      values: { motivoId: MOTIVO_ID, motivoTexto: 'Capturado por error' },
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    // Verificación clave: el header NO se envía.
    expect(idempotencyVisto).toBeNull();
    expect(bodyVisto).toMatchObject({
      motivoId: MOTIVO_ID,
      motivoTexto: 'Capturado por error',
    });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: comprasKeys.all });
  });

  it('422 MOTIVO_NO_APLICA_A_ELIMINACION: bubblea ApiError', async () => {
    mswServer.use(
      http.post(
        `*/api/v1/compras/requisiciones/${RQ_ID}/eliminar`,
        () =>
          HttpResponse.json(
            {
              type: 'about:blank',
              title: 'Validación',
              status: 422,
              code: 'MOTIVO_NO_APLICA_A_ELIMINACION',
            },
            {
              status: 422,
              headers: { 'Content-Type': 'application/problem+json' },
            },
          ),
      ),
    );

    const { result } = renderHook(() => useEliminarRequisicion(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      requisicionId: RQ_ID,
      values: { motivoId: MOTIVO_ID, motivoTexto: null },
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(422);
    expect(err.code).toBe('MOTIVO_NO_APLICA_A_ELIMINACION');
  });
});

describe('useCancelarRequisicion', () => {
  it('204 OK: REQUIERE Idempotency-Key e invalida familia', async () => {
    let idempotencyVisto: string | null = null;

    mswServer.use(
      http.post(
        `*/api/v1/compras/requisiciones/${RQ_ID}/cancelar`,
        ({ request }) => {
          idempotencyVisto = request.headers.get('Idempotency-Key');
          return new HttpResponse(null, { status: 204 });
        },
      ),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useCancelarRequisicion(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      requisicionId: RQ_ID,
      values: { motivoId: MOTIVO_ID, motivoTexto: 'Proveedor dejó de existir' },
      idempotencyKey: 'idem-cancel-1',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(idempotencyVisto).toBe('idem-cancel-1');
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: comprasKeys.all });
  });

  it('422 CANCELAR_FALLO: bubblea ApiError con traceId (downstream falló)', async () => {
    mswServer.use(
      http.post(
        `*/api/v1/compras/requisiciones/${RQ_ID}/cancelar`,
        () =>
          HttpResponse.json(
            {
              type: 'about:blank',
              title: 'Cancelación falló downstream',
              status: 422,
              code: 'CANCELAR_FALLO',
              traceId: 'trace-cancel-fail-001',
              detail:
                'No se pudieron liberar las reservas de stock. Revisa con soporte.',
            },
            {
              status: 422,
              headers: { 'Content-Type': 'application/problem+json' },
            },
          ),
      ),
    );

    const { result } = renderHook(() => useCancelarRequisicion(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      requisicionId: RQ_ID,
      values: { motivoId: MOTIVO_ID, motivoTexto: null },
      idempotencyKey: 'idem-cancel-2',
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as {
      status: number;
      code?: string;
      traceId?: string;
    };
    expect(err.status).toBe(422);
    expect(err.code).toBe('CANCELAR_FALLO');
    expect(err.traceId).toBe('trace-cancel-fail-001');
  });
});
