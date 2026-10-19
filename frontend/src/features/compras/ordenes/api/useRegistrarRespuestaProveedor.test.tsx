import { describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper, createTestQueryClient } from '@/test/test-query-client';
import { useRegistrarRespuestaProveedor } from '@/features/compras/ordenes/api/useRegistrarRespuestaProveedor';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type { RegistrarRespuestaProveedorValues } from '@/features/compras/ordenes/schemas/respuesta-proveedor';

const COMMAND: RegistrarRespuestaProveedorValues = {
  tipo: 1,
  fechaRespuesta: '2026-10-17',
  evidencia: 'Correo P-881',
  compromisos: [
    { lineaOrdenCompraId: '0198a7c2-1111-7000-8000-000000000001', cantidad: 10, fechaPrometida: '2026-10-20' },
  ],
};

describe('useRegistrarRespuestaProveedor', () => {
  it('POST a respuestas-proveedor con body e Idempotency-Key e invalida el detalle', async () => {
    let method = '';
    let body: unknown = null;
    let idempotencyKey: string | null = null;
    mswServer.use(
      http.post('*/api/v1/compras/ordenes/oc-1/respuestas-proveedor', async ({ request }) => {
        method = request.method;
        body = await request.json();
        idempotencyKey = request.headers.get('Idempotency-Key');
        return HttpResponse.json({ id: 'r-1', numero: 1 }, { status: 201 });
      }),
    );

    const { result } = renderHook(() => useRegistrarRespuestaProveedor(), {
      wrapper: createQueryWrapper(),
    });

    await act(async () => {
      await result.current.mutateAsync({
        ordenCompraId: 'oc-1',
        command: COMMAND,
        idempotencyKey: 'key-123',
      });
    });

    expect(method).toBe('POST');
    expect(body).toEqual(COMMAND);
    expect(idempotencyKey).toBe('key-123');
  });

  it('invalida el detalle y la bandeja al terminar', async () => {
    mswServer.use(
      http.post('*/api/v1/compras/ordenes/oc-1/respuestas-proveedor', () =>
        HttpResponse.json({ id: 'r-1', numero: 1 }, { status: 201 }),
      ),
    );
    const client = createTestQueryClient();
    const spy = vi.spyOn(client, 'invalidateQueries');
    const { result } = renderHook(() => useRegistrarRespuestaProveedor(), {
      wrapper: createQueryWrapper(client),
    });
    await act(async () => {
      await result.current.mutateAsync({ ordenCompraId: 'oc-1', command: COMMAND, idempotencyKey: 'k' });
    });
    expect(spy).toHaveBeenCalledWith({ queryKey: ordenesKeys.detail('oc-1') });
    expect(spy).toHaveBeenCalledWith({ queryKey: ordenesKeys.all });
  });
});
