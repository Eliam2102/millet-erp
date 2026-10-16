import { describe, expect, it } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useActualizarNumeroPedimento } from '@/features/compras/ordenes/api/useActualizarNumeroPedimento';
import { ApiError } from '@/lib/api';

const COMMAND = {
  numeroPedimento: '264738135001234',
  numeroBl: 'MAEU123456',
  fechaEstimadaArribo: '2026-11-30',
  agenteAduanalNaviera: 'Agencia Aduanal Norte',
};

describe('useActualizarNumeroPedimento', () => {
  it('PATCH al endpoint de pedimento con los cuatro campos y Idempotency-Key', async () => {
    let body: unknown = null;
    let idempotencyKey: string | null = null;

    mswServer.use(
      http.patch('*/api/v1/compras/ordenes/oc-1/numero-pedimento', async ({ request }) => {
        body = await request.json();
        idempotencyKey = request.headers.get('Idempotency-Key');
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const { result } = renderHook(() => useActualizarNumeroPedimento(), {
      wrapper: createQueryWrapper(),
    });

    await act(async () => {
      await result.current.mutateAsync({
        ordenCompraId: 'oc-1',
        command: COMMAND,
        idempotencyKey: 'key-123',
      });
    });

    expect(body).toEqual(COMMAND);
    expect(idempotencyKey).toBe('key-123');
  });

  it('propaga el ApiError del servidor (422) con su código y detalle', async () => {
    mswServer.use(
      http.patch('*/api/v1/compras/ordenes/oc-1/numero-pedimento', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Regla de negocio',
            status: 422,
            code: 'LINEA_OC_FRACCION_SOLO_COMERCIO_EXTERIOR',
            detail: 'La fracción solo aplica a comercio exterior.',
          },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );

    const { result } = renderHook(() => useActualizarNumeroPedimento(), {
      wrapper: createQueryWrapper(),
    });

    let error: unknown = null;
    await act(async () => {
      try {
        await result.current.mutateAsync({
          ordenCompraId: 'oc-1',
          command: COMMAND,
          idempotencyKey: 'key-456',
        });
      } catch (e) {
        error = e;
      }
    });

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).code).toBe('LINEA_OC_FRACCION_SOLO_COMERCIO_EXTERIOR');
    expect((error as ApiError).problem.detail).toBe(
      'La fracción solo aplica a comercio exterior.',
    );
  });
});
