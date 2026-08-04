import { describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import {
  createQueryWrapper,
  createTestQueryClient,
} from '@/test/test-query-client';
import {
  useActualizarCanalVenta,
  useCanalesVentaAdmin,
  useCrearCanalVenta,
} from '@/modules/administracion/api/canales-venta';
import { adminKeys } from '@/modules/administracion/api/keys';

const BASE = '*/api/v1/admin/canales-venta';

describe('useCanalesVentaAdmin', () => {
  it('200: lista completa; el filtro estatus viaja en el query string', async () => {
    let urlVista = '';
    mswServer.use(
      http.get(BASE, ({ request }) => {
        urlVista = request.url;
        return HttpResponse.json([
          {
            id: 1,
            nombre: 'Tienda Cancún',
            estatus: 0,
            version: 1,
            claveAw: 'CANCUN',
          },
          {
            id: 10,
            nombre: 'Administración',
            estatus: 1,
            version: 2,
            claveAw: null,
          },
        ]);
      }),
    );

    const { result } = renderHook(() => useCanalesVentaAdmin(1), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toHaveLength(2);
    expect(result.current.data?.[0].claveAw).toBe('CANCUN');
    expect(urlVista).toContain('estatus=1');
  });
});

describe('useCrearCanalVenta', () => {
  it('201: POST con Idempotency-Key; invalida admin + lookup de Facturación', async () => {
    let bodyVisto: unknown = null;
    let idemVisto: string | null = null;
    mswServer.use(
      http.post(BASE, async ({ request }) => {
        bodyVisto = await request.json();
        idemVisto = request.headers.get('Idempotency-Key');
        return HttpResponse.json(
          {
            id: 11,
            nombre: 'Tienda Playa',
            estatus: 0,
            version: 1,
            claveAw: 'PLAYA',
          },
          { status: 201 },
        );
      }),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useCrearCanalVenta(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      command: { nombre: 'Tienda Playa', claveAw: 'PLAYA' },
      idempotencyKey: 'idem-1',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.id).toBe(11);
    expect(idemVisto).toBe('idem-1');
    expect(bodyVisto).toMatchObject({ nombre: 'Tienda Playa', claveAw: 'PLAYA' });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: adminKeys.canalesVenta(),
    });
    // Los selectores de Facturación consumen el lookup — se invalida
    // con la clave laxa cross-módulo (mismo patrón que sucursales).
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: ['facturacion', 'catalogos', 'canales-venta'],
    });
  });

  it('409 CANAL_VENTA_NOMBRE_DUPLICADO propaga al caller', async () => {
    mswServer.use(
      http.post(BASE, () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Conflicto',
            status: 409,
            code: 'CANAL_VENTA_NOMBRE_DUPLICADO',
          },
          {
            status: 409,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useCrearCanalVenta(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      command: { nombre: 'Tienda Cancún' },
      idempotencyKey: 'idem-2',
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(409);
    expect(err.code).toBe('CANAL_VENTA_NOMBRE_DUPLICADO');
  });
});

describe('useActualizarCanalVenta', () => {
  it('200: PATCH parcial de nombre/claveAw/estatus', async () => {
    let bodyVisto: unknown = null;
    mswServer.use(
      http.patch(`${BASE}/1`, async ({ request }) => {
        bodyVisto = await request.json();
        return HttpResponse.json({
          id: 1,
          nombre: 'Tienda Cancún Centro',
          estatus: 0,
          version: 2,
          claveAw: null,
        });
      }),
    );

    const { result } = renderHook(() => useActualizarCanalVenta(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      id: 1,
      payload: { nombre: 'Tienda Cancún Centro', limpiarClaveAw: true },
      idempotencyKey: 'idem-3',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.version).toBe(2);
    expect(bodyVisto).toEqual({
      nombre: 'Tienda Cancún Centro',
      limpiarClaveAw: true,
    });
  });

  it('200: PATCH { estatus } desactiva sin tocar nombre/clave', async () => {
    let bodyVisto: unknown = null;
    mswServer.use(
      http.patch(`${BASE}/9`, async ({ request }) => {
        bodyVisto = await request.json();
        return HttpResponse.json({
          id: 9,
          nombre: 'Planta Pintura',
          estatus: 1,
          version: 3,
          claveAw: null,
        });
      }),
    );

    const { result } = renderHook(() => useActualizarCanalVenta(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      id: 9,
      payload: { estatus: 1 },
      idempotencyKey: 'idem-4',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.estatus).toBe(1);
    expect(bodyVisto).toEqual({ estatus: 1 });
  });
});
