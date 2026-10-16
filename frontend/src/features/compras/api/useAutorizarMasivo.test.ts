import { describe, expect, it } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAutorizarMasivo } from '@/features/compras/api/useAutorizarMasivo';
import { NivelAutorizacion } from '@/features/compras/api/types';

describe('useAutorizarMasivo', () => {
  it('POST al endpoint masivo con ids, nivel y Idempotency-Key; devuelve resultado por id', async () => {
    let body: unknown = null;
    let idempotencyKey: string | null = null;

    mswServer.use(
      http.post(
        '*/api/v1/compras/requisiciones/autorizaciones-masivas',
        async ({ request }) => {
          body = await request.json();
          idempotencyKey = request.headers.get('Idempotency-Key');
          return HttpResponse.json({
            resultados: [
              { id: 'rq-1', ok: true, errorCodigo: null, mensaje: null },
              {
                id: 'rq-2',
                ok: false,
                errorCodigo: 'REQUISICION_NO_ENCONTRADA',
                mensaje: 'No se encontró requisición',
              },
            ],
          });
        },
      ),
    );

    const { result } = renderHook(() => useAutorizarMasivo(), {
      wrapper: createQueryWrapper(),
    });

    let respuesta: Awaited<ReturnType<typeof result.current.mutateAsync>> | undefined;
    await act(async () => {
      respuesta = await result.current.mutateAsync({
        ids: ['rq-1', 'rq-2'],
        nivel: NivelAutorizacion.Nivel1,
        idempotencyKey: 'key-123',
      });
    });

    expect(body).toEqual({ ids: ['rq-1', 'rq-2'], nivel: 1, notas: null });
    expect(idempotencyKey).toBe('key-123');
    expect(respuesta?.resultados).toHaveLength(2);
    expect(respuesta?.resultados[0]).toEqual({
      id: 'rq-1',
      ok: true,
      errorCodigo: null,
      mensaje: null,
    });
    expect(respuesta?.resultados[1].ok).toBe(false);
    expect(respuesta?.resultados[1].errorCodigo).toBe('REQUISICION_NO_ENCONTRADA');
  });

  it('403 del endpoint: la mutación falla (no hay resultados por id)', async () => {
    mswServer.use(
      http.post(
        '*/api/v1/compras/requisiciones/autorizaciones-masivas',
        () =>
          HttpResponse.json(
            { type: 'about:blank', title: 'Permiso denegado', status: 403 },
            { status: 403, headers: { 'Content-Type': 'application/problem+json' } },
          ),
      ),
    );

    const { result } = renderHook(() => useAutorizarMasivo(), {
      wrapper: createQueryWrapper(),
    });

    await act(async () => {
      await expect(
        result.current.mutateAsync({
          ids: ['rq-1'],
          nivel: NivelAutorizacion.Nivel2,
          idempotencyKey: 'key-403',
        }),
      ).rejects.toBeDefined();
    });
  });
});
