import { describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import {
  createQueryWrapper,
  createTestQueryClient,
} from '@/test/test-query-client';
import { useAsignarPuestoASucursal } from '@/modules/administracion/api/sucursal-puestos';
import { adminKeys } from '@/modules/administracion/api/keys';

describe('useAsignarPuestoASucursal', () => {
  it('envía departamentoId en el body y luego invalida sucursalPuestosList', async () => {
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
      queryKey: adminKeys.sucursalPuestosList('s-1'),
    });
  });
});
