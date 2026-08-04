import { describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import {
  createQueryWrapper,
  createTestQueryClient,
} from '@/test/test-query-client';
import {
  useActualizarSucursal,
  useCrearSucursal,
  useDesactivarSucursal,
} from '@/modules/administracion/api/sucursales';
import { adminKeys } from '@/modules/administracion/api/keys';

describe('useCrearSucursal', () => {
  it('201: invalida detalle de empresa + catálogos.sucursales', async () => {
    let bodyVisto: unknown = null;
    mswServer.use(
      http.post('*/api/v1/admin/empresas/sucursales', async ({ request }) => {
        bodyVisto = await request.json();
        return HttpResponse.json(
          { id: 's-1', clave: 'MID', nombre: 'Mérida', estatus: 0, version: 1 },
          { status: 201 },
        );
      }),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useCrearSucursal(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      empresaId: 'e-1',
      command: { clave: 'MID', nombre: 'Mérida' },
      idempotencyKey: 'idem-1',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(bodyVisto).toMatchObject({ clave: 'MID', nombre: 'Mérida' });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: adminKeys.empresa('e-1'),
    });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: ['catalogos', 'sucursales'],
    });
  });

  it('409 SUCURSAL_CLAVE_DUPLICADA propaga al caller', async () => {
    mswServer.use(
      http.post('*/api/v1/admin/empresas/sucursales', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Conflicto',
            status: 409,
            code: 'SUCURSAL_CLAVE_DUPLICADA',
          },
          {
            status: 409,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useCrearSucursal(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      empresaId: 'e-1',
      command: { clave: 'MID', nombre: 'Mérida' },
      idempotencyKey: 'idem-2',
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(409);
    expect(err.code).toBe('SUCURSAL_CLAVE_DUPLICADA');
  });
});

describe('useActualizarSucursal', () => {
  it('200: PATCH envía solo {nombre} (clave no se puede cambiar)', async () => {
    let bodyVisto: unknown = null;
    mswServer.use(
      http.patch('*/api/v1/admin/empresas/sucursales/s-1', async ({ request }) => {
        bodyVisto = await request.json();
        return HttpResponse.json({
          id: 's-1',
          clave: 'MID',
          nombre: 'Mérida actualizada',
          estatus: 0,
          version: 2,
        });
      }),
    );

    const { result } = renderHook(() => useActualizarSucursal(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      empresaId: 'e-1',
      id: 's-1',
      payload: { nombre: 'Mérida actualizada' },
      idempotencyKey: 'idem-3',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(bodyVisto).toEqual({ nombre: 'Mérida actualizada' });
  });
});

describe('useDesactivarSucursal', () => {
  it('200: POST /desactivar invalida detalle de empresa', async () => {
    mswServer.use(
      http.post('*/api/v1/admin/empresas/sucursales/s-1/desactivar', () =>
        HttpResponse.json({
          id: 's-1',
          clave: 'MID',
          nombre: 'Mérida',
          estatus: 1,
          version: 2,
        }),
      ),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useDesactivarSucursal(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      empresaId: 'e-1',
      id: 's-1',
      idempotencyKey: 'idem-4',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.estatus).toBe(1);
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: adminKeys.empresa('e-1'),
    });
  });
});
