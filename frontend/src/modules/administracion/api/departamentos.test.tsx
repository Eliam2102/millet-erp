import { describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import {
  createQueryWrapper,
  createTestQueryClient,
} from '@/test/test-query-client';
import {
  useActualizarDepartamento,
  useCrearDepartamento,
} from '@/modules/administracion/api/departamentos';
import { adminKeys } from '@/modules/administracion/api/keys';

describe('useCrearDepartamento', () => {
  it('201: invalida detalle de empresa + catálogos.departamentos', async () => {
    let bodyVisto: unknown = null;
    mswServer.use(
      http.post('*/api/v1/admin/departamentos', async ({ request }) => {
        bodyVisto = await request.json();
        return HttpResponse.json(
          {
            id: 'd-1',
            clave: 'COMP',
            nombre: 'Compras',
            estatus: 0,
            version: 1,
          },
          { status: 201 },
        );
      }),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useCrearDepartamento(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      empresaId: 'e-1',
      command: { clave: 'COMP', nombre: 'Compras' },
      idempotencyKey: 'idem-1',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(bodyVisto).toMatchObject({ clave: 'COMP', nombre: 'Compras' });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: adminKeys.empresa('e-1'),
    });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: ['catalogos', 'departamentos'],
    });
  });

  it('409 DEPARTAMENTO_CLAVE_DUPLICADA propaga al caller', async () => {
    mswServer.use(
      http.post('*/api/v1/admin/departamentos', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Conflicto',
            status: 409,
            code: 'DEPARTAMENTO_CLAVE_DUPLICADA',
          },
          {
            status: 409,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useCrearDepartamento(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      empresaId: 'e-1',
      command: { clave: 'DUP', nombre: 'Duplicado' },
      idempotencyKey: 'idem-2',
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.code).toBe('DEPARTAMENTO_CLAVE_DUPLICADA');
  });
});

describe('useActualizarDepartamento', () => {
  it('200: PATCH envía solo {nombre}', async () => {
    let bodyVisto: unknown = null;
    mswServer.use(
      http.patch('*/api/v1/admin/departamentos/d-1', async ({ request }) => {
        bodyVisto = await request.json();
        return HttpResponse.json({
          id: 'd-1',
          clave: 'COMP',
          nombre: 'Compras actualizado',
          estatus: 0,
          version: 2,
        });
      }),
    );

    const { result } = renderHook(() => useActualizarDepartamento(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      empresaId: 'e-1',
      id: 'd-1',
      payload: { nombre: 'Compras actualizado' },
      idempotencyKey: 'idem-3',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(bodyVisto).toEqual({ nombre: 'Compras actualizado' });
  });
});
