import { FormData as NativeFormData, Response as NativeResponse } from 'undici';
import { File as NodeFile, Blob as NodeBlob } from 'node:buffer';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper, createTestQueryClient } from '@/test/test-query-client';
import { useRegistrarRespuestaProveedor } from '@/features/compras/ordenes/api/useRegistrarRespuestaProveedor';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type { RegistrarRespuestaProveedorValues } from '@/features/compras/ordenes/schemas/respuesta-proveedor';

// Usar archivos y multipart de Node juntos; jsdom no implementa el stream del archivo.
beforeEach(() => { vi.stubGlobal('FormData', NativeFormData); vi.stubGlobal('Response', NativeResponse); vi.stubGlobal('File', NodeFile); vi.stubGlobal('Blob', NodeBlob); });
afterEach(() => vi.unstubAllGlobals());

const COMMAND: RegistrarRespuestaProveedorValues = {
  archivo: new NodeFile(["%PDF-1.4 DEMO ficticio"], "DEMO.pdf", { type: "application/pdf" }) as unknown as File,
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
        const form = await request.formData();
        body = JSON.parse(form.get('datos') as string);
        expect((form.get('archivo') as File).name).toBe('DEMO.pdf');
        expect(await (form.get('archivo') as File).text()).toBe('%PDF-1.4 DEMO ficticio');
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
    expect(body).toEqual({ tipo: COMMAND.tipo, fechaRespuesta: COMMAND.fechaRespuesta, evidencia: COMMAND.evidencia, compromisos: COMMAND.compromisos });
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

it('reintenta con los mismos bytes multipart y la misma clave', async () => {
  const bodies: string[] = [];
  mswServer.use(http.post('*/api/v1/compras/ordenes/oc-1/respuestas-proveedor', async ({ request }) => {
    bodies.push(await request.text());
    expect(request.headers.get('Idempotency-Key')).toBe('DEMO-retry');
    return bodies.length === 1
      ? HttpResponse.json({ status: 409, title: 'En proceso', code: 'IDEMPOTENCY_IN_PROGRESS' }, { status: 409, headers: { 'Retry-After': '0' } })
      : HttpResponse.json({ id: 'r-1', numero: 1 }, { status: 201 });
  }));
  const { result } = renderHook(() => useRegistrarRespuestaProveedor(), { wrapper: createQueryWrapper() });
  await act(async () => { await result.current.mutateAsync({ ordenCompraId: 'oc-1', command: COMMAND, idempotencyKey: 'DEMO-retry' }); });
  expect(bodies).toHaveLength(2);
  expect(bodies[0]).toBe(bodies[1]);
});
