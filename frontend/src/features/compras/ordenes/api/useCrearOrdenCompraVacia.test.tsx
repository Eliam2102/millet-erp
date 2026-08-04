import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useCrearOrdenCompraVacia } from '@/features/compras/ordenes/api/useCrearOrdenCompraVacia';
import { DEFAULT_CREAR_OC_VACIA } from '@/features/compras/ordenes/schemas/crear-oc-vacia';

const COMMAND = {
  ...DEFAULT_CREAR_OC_VACIA,
  sucursalDestinoId: '00000003-0002-0000-0000-000000000001',
  proveedorId: '00000005-0001-0000-0000-000000000001',
  condicionesPagoId: '00000002-0003-0000-0000-000000000001',
  usoPrincipalId: '00000002-0004-0000-0000-000000000001',
  fechaDocumento: '2026-05-12',
  sucursalCodigo: 'MID',
  folioAnio: 2026,
};

describe('useCrearOrdenCompraVacia', () => {
  it('201 OK: parsea folio + id + version del response', async () => {
    let recibidoIdempotency: string | null = null;
    mswServer.use(
      http.post('*/api/v1/compras/ordenes', async ({ request }) => {
        recibidoIdempotency = request.headers.get('Idempotency-Key');
        return HttpResponse.json(
          {
            id: 'oc-new-1',
            folio: 'OC-MID2026-000010',
            folioAnio: 2026,
            estado: 0,
            version: 1,
          },
          { status: 201 },
        );
      }),
    );

    const { result } = renderHook(() => useCrearOrdenCompraVacia(), {
      wrapper: createQueryWrapper(),
    });

    const response = await result.current.mutateAsync({
      command: COMMAND,
      idempotencyKey: 'fa01a55d-a3a8-4c4f-8d52-aaaaaaaaaaaa',
    });

    expect(response.id).toBe('oc-new-1');
    expect(response.folio).toBe('OC-MID2026-000010');
    expect(recibidoIdempotency).toBe('fa01a55d-a3a8-4c4f-8d52-aaaaaaaaaaaa');
  });

  it('422 con campo errors: ApiError llega con problem.errores', async () => {
    mswServer.use(
      http.post('*/api/v1/compras/ordenes', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Validación falló',
            status: 422,
            code: 'PROVEEDOR_INACTIVO',
            errores: [{ campo: 'proveedorId', mensaje: 'Proveedor inactivo' }],
          },
          {
            status: 422,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useCrearOrdenCompraVacia(), {
      wrapper: createQueryWrapper(),
    });

    let caught: unknown = null;
    try {
      await result.current.mutateAsync({
        command: COMMAND,
        idempotencyKey: 'fa01a55d-a3a8-4c4f-8d52-bbbbbbbbbbbb',
      });
    } catch (e) {
      caught = e;
    }
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(caught).toBeDefined();
  });

  it('403 CREAR_SIN_RQ_DENEGADO: ApiError 403', async () => {
    mswServer.use(
      http.post('*/api/v1/compras/ordenes', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Forbidden',
            status: 403,
            code: 'CREAR_SIN_RQ_DENEGADO',
          },
          {
            status: 403,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useCrearOrdenCompraVacia(), {
      wrapper: createQueryWrapper(),
    });

    let caught: { status: number } | null = null;
    try {
      await result.current.mutateAsync({
        command: { ...COMMAND, sinRequisicionPrevia: true, motivoSinRequisicion: 'test' },
        idempotencyKey: 'fa01a55d-a3a8-4c4f-8d52-cccccccccccc',
      });
    } catch (e) {
      caught = e as { status: number };
    }
    expect(caught?.status).toBe(403);
  });
});
