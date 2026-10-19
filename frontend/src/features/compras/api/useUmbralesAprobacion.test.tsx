import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  useGuardarUmbral,
  useUmbralesAprobacion,
} from '@/features/compras/api/useUmbralesAprobacion';
import type { UmbralAprobacionResponse } from '@/features/compras/api/types';

const DEPTO_ID = '11111111-1111-4111-8111-111111111111';
const KEY = '44444444-4444-4444-8444-444444444444';

const UMBRAL: UmbralAprobacionResponse = {
  departamentoId: DEPTO_ID,
  umbralMenorCuantia: 5000,
  umbralMonto: 20000,
  moneda: 'MXN',
  vigenteDesde: '2026-10-15',
  modoNivel2: 'Ambos',
  montoMinimoDisponibilidad: null,
};

describe('useUmbralesAprobacion', () => {
  it('devuelve los umbrales vigentes del backend', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/umbrales-aprobacion', () =>
        HttpResponse.json([UMBRAL]),
      ),
    );
    const { result } = renderHook(() => useUmbralesAprobacion(), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toEqual([UMBRAL]);
  });
});

describe('useGuardarUmbral', () => {
  it('hace PUT con el body, Idempotency-Key y devuelve la vigencia guardada', async () => {
    let bodyVisto: unknown = null;
    let keyVista: string | null = null;
    mswServer.use(
      http.put('*/api/v1/compras/umbrales-aprobacion', async ({ request }) => {
        bodyVisto = await request.json();
        keyVista = request.headers.get('Idempotency-Key');
        return HttpResponse.json(UMBRAL);
      }),
    );
    const { result } = renderHook(() => useGuardarUmbral(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      values: {
        departamentoId: DEPTO_ID,
        umbralMenorCuantia: 5000,
        umbralMonto: 20000,
        moneda: 'MXN',
      },
      idempotencyKey: KEY,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(keyVista).toBe(KEY);
    expect(bodyVisto).toEqual({
      departamentoId: DEPTO_ID,
      umbralMenorCuantia: 5000,
      umbralMonto: 20000,
      moneda: 'MXN',
    });
    expect(result.current.data).toEqual(UMBRAL);
  });

  it('propaga el 400 del backend (N1 >= N2) como error', async () => {
    mswServer.use(
      http.put('*/api/v1/compras/umbrales-aprobacion', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Validación',
            status: 400,
            detail: 'El umbral N1 (menor cuantía) debe ser menor que el umbral N2.',
          },
          {
            status: 400,
            headers: { 'content-type': 'application/problem+json' },
          },
        ),
      ),
    );
    const { result } = renderHook(() => useGuardarUmbral(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      values: {
        departamentoId: DEPTO_ID,
        umbralMenorCuantia: 9000,
        umbralMonto: 8000,
        moneda: 'MXN',
      },
      idempotencyKey: KEY,
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.error?.message).toContain('Validación');
  });
});
