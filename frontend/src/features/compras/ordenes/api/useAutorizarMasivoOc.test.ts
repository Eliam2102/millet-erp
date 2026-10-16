import { describe, expect, it } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAutorizarMasivoOc } from '@/features/compras/ordenes/api/useAutorizarMasivoOc';
import { NivelAutorizacion } from '@/features/compras/ordenes/schemas/autorizar';

describe('useAutorizarMasivoOc', () => {
  it('POST al endpoint masivo de OC con ids, nivel numérico y Idempotency-Key; devuelve resultado por id', async () => {
    let body: unknown = null;
    let idempotencyKey: string | null = null;

    mswServer.use(
      http.post(
        '*/api/v1/compras/ordenes/autorizaciones-masivas',
        async ({ request }) => {
          body = await request.json();
          idempotencyKey = request.headers.get('Idempotency-Key');
          return HttpResponse.json({
            resultados: [
              { id: 'oc-1', ok: true, errorCodigo: null, mensaje: null },
              {
                id: 'oc-2',
                ok: false,
                errorCodigo: 'OC_NO_ENCONTRADA',
                mensaje: 'No se encontró la orden de compra',
              },
            ],
          });
        },
      ),
    );

    const { result } = renderHook(() => useAutorizarMasivoOc(), {
      wrapper: createQueryWrapper(),
    });

    let respuesta: Awaited<ReturnType<typeof result.current.mutateAsync>> | undefined;
    await act(async () => {
      respuesta = await result.current.mutateAsync({
        ids: ['oc-1', 'oc-2'],
        nivel: NivelAutorizacion.Nivel1,
        idempotencyKey: 'key-oc-123',
      });
    });

    expect(body).toEqual({ ids: ['oc-1', 'oc-2'], nivel: 1, notas: null });
    expect(idempotencyKey).toBe('key-oc-123');
    expect(respuesta?.resultados).toHaveLength(2);
    expect(respuesta?.resultados[0].ok).toBe(true);
    expect(respuesta?.resultados[1].ok).toBe(false);
    expect(respuesta?.resultados[1].errorCodigo).toBe('OC_NO_ENCONTRADA');
  });
});
