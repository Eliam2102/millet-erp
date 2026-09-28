import { describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import {
  createQueryWrapper,
  createTestQueryClient,
} from '@/test/test-query-client';
import {
  useAsignarPuestoASucursal,
  useActualizarRolSugeridoAsignacion,
  useDesactivarAsignacionSucursalPuesto,
  usePuestosDeSucursal,
  useReactivarAsignacionSucursalPuesto,
} from '@/modules/administracion/api/sucursal-puestos';
import { adminKeys } from '@/modules/administracion/api/keys';

describe('useAsignarPuestoASucursal', () => {
  it('envía departamentoId en el body y luego invalida todas las variantes de sucursalPuestos', async () => {
    let bodyVisto: unknown = null;
    let urlVisto = '';

    mswServer.use(
      http.post(
        '*/api/v1/admin/empresas/sucursales/:sucursalId/puestos/:puestoId',
        async ({ request, params }) => {
          bodyVisto = await request.json();
          urlVisto = `/api/v1/admin/empresas/sucursales/${params.sucursalId}/puestos/${params.puestoId}`;
          return HttpResponse.json(
            {
              sucursalId: params.sucursalId,
              puestoId: params.puestoId,
              puestoClave: 'GER',
              puestoNombre: 'Gerente General',
              departamentoId: 'd-123',
              departamentoNombre: 'Operaciones',
              rolSugeridoId: null,
              rolSugeridoEfectivoId: null,
              estatus: 0,
              version: 1,
            },
            { status: 201 },
          );
        },
      ),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useAsignarPuestoASucursal(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({
      sucursalId: 's-1',
      puestoId: 'p-1',
      departamentoId: 'd-123',
      idempotencyKey: 'idem-sxp-1',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVisto).toBe('/api/v1/admin/empresas/sucursales/s-1/puestos/p-1');
    expect(bodyVisto).toEqual({ departamentoId: 'd-123' });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: adminKeys.sucursalPuestosSucursal('s-1'),
    });
  });

  it('manda rolSugeridoId cuando se especifica (excepción por asignación)', async () => {
    let bodyVisto: unknown = null;

    mswServer.use(
      http.post(
        '*/api/v1/admin/empresas/sucursales/:sucursalId/puestos/:puestoId',
        async ({ request, params }) => {
          bodyVisto = await request.json();
          return HttpResponse.json(
            {
              sucursalId: params.sucursalId,
              puestoId: params.puestoId,
              puestoClave: 'GER',
              puestoNombre: 'Gerente General',
              departamentoId: 'd-123',
              departamentoNombre: 'Operaciones',
              rolSugeridoId: 'rol-1',
              rolSugeridoEfectivoId: 'rol-1',
              estatus: 0,
              version: 1,
            },
            { status: 201 },
          );
        },
      ),
    );

    const { result } = renderHook(() => useAsignarPuestoASucursal(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      sucursalId: 's-1',
      puestoId: 'p-1',
      departamentoId: 'd-123',
      rolSugeridoId: 'rol-1',
      idempotencyKey: 'idem-sxp-2',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(bodyVisto).toEqual({ departamentoId: 'd-123', rolSugeridoId: 'rol-1' });
  });
});

describe('usePuestosDeSucursal', () => {
  it('agrega departamentoId como query param cuando se especifica', async () => {
    let urlVista = '';

    mswServer.use(
      http.get(
        '*/api/v1/admin/empresas/sucursales/:sucursalId/puestos',
        ({ request }) => {
          urlVista = request.url;
          return HttpResponse.json({ items: [], total: 0 });
        },
      ),
    );

    const { result } = renderHook(
      () => usePuestosDeSucursal('s-1', 'd-99'),
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(urlVista).toContain('/puestos?departamentoId=d-99');
  });
});

describe('useDesactivarAsignacionSucursalPuesto / useReactivarAsignacionSucursalPuesto', () => {
  it('identifican la fila por (sucursal, puesto, departamento) en la URL', async () => {
    let urlDesactivar = '';
    let urlReactivar = '';

    mswServer.use(
      http.post(
        '*/api/v1/admin/empresas/sucursales/:sucursalId/puestos/:puestoId/departamentos/:departamentoId/desactivar',
        ({ request }) => {
          urlDesactivar = request.url;
          return HttpResponse.json({
            sucursalId: 's-1',
            puestoId: 'p-1',
            puestoClave: 'GER',
            puestoNombre: 'Gerente',
            departamentoId: 'd-1',
            departamentoNombre: 'Almacén',
            rolSugeridoId: null,
            rolSugeridoEfectivoId: null,
            estatus: 1,
            version: 2,
          });
        },
      ),
      http.post(
        '*/api/v1/admin/empresas/sucursales/:sucursalId/puestos/:puestoId/departamentos/:departamentoId/reactivar',
        ({ request }) => {
          urlReactivar = request.url;
          return HttpResponse.json({
            sucursalId: 's-1',
            puestoId: 'p-1',
            puestoClave: 'GER',
            puestoNombre: 'Gerente',
            departamentoId: 'd-1',
            departamentoNombre: 'Almacén',
            rolSugeridoId: null,
            rolSugeridoEfectivoId: null,
            estatus: 0,
            version: 3,
          });
        },
      ),
    );

    const { result: desactivar } = renderHook(
      () => useDesactivarAsignacionSucursalPuesto(),
      { wrapper: createQueryWrapper() },
    );
    desactivar.current.mutate({
      sucursalId: 's-1',
      puestoId: 'p-1',
      departamentoId: 'd-1',
      idempotencyKey: 'idem-1',
    });
    await waitFor(() => expect(desactivar.current.isSuccess).toBe(true));
    expect(urlDesactivar).toContain(
      '/puestos/p-1/departamentos/d-1/desactivar',
    );

    const { result: reactivar } = renderHook(
      () => useReactivarAsignacionSucursalPuesto(),
      { wrapper: createQueryWrapper() },
    );
    reactivar.current.mutate({
      sucursalId: 's-1',
      puestoId: 'p-1',
      departamentoId: 'd-1',
      idempotencyKey: 'idem-2',
    });
    await waitFor(() => expect(reactivar.current.isSuccess).toBe(true));
    expect(urlReactivar).toContain(
      '/puestos/p-1/departamentos/d-1/reactivar',
    );
  });
});

describe('useActualizarRolSugeridoAsignacion', () => {
  it('hace PATCH con rolSugeridoId y devuelve el rolSugeridoEfectivoId actualizado', async () => {
    let metodoVisto = '';
    let bodyVisto: unknown = null;

    mswServer.use(
      http.patch(
        '*/api/v1/admin/empresas/sucursales/:sucursalId/puestos/:puestoId/departamentos/:departamentoId',
        async ({ request }) => {
          metodoVisto = request.method;
          bodyVisto = await request.json();
          return HttpResponse.json({
            sucursalId: 's-1',
            puestoId: 'p-1',
            puestoClave: 'GER',
            puestoNombre: 'Gerente',
            departamentoId: 'd-1',
            departamentoNombre: 'Almacén',
            rolSugeridoId: 'rol-2',
            rolSugeridoEfectivoId: 'rol-2',
            estatus: 0,
            version: 4,
          });
        },
      ),
    );

    const { result } = renderHook(() => useActualizarRolSugeridoAsignacion(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({
      sucursalId: 's-1',
      puestoId: 'p-1',
      departamentoId: 'd-1',
      rolSugeridoId: 'rol-2',
      idempotencyKey: 'idem-3',
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(metodoVisto).toBe('PATCH');
    expect(bodyVisto).toEqual({ rolSugeridoId: 'rol-2' });
    expect(result.current.data?.rolSugeridoEfectivoId).toBe('rol-2');
  });
});
