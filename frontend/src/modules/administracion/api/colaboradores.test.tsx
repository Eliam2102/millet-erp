import { describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper, createTestQueryClient } from '@/test/test-query-client';
import { useAltaColaborador } from '@/modules/administracion/api/empleados';
import { adminKeys } from '@/modules/administracion/api/keys';

describe('useAltaColaborador', () => {
  it('usa el alta unificada con clave de idempotencia e invalida empleados', async () => {
    let body: unknown;
    let key: string | null = null;
    mswServer.use(
      http.post('*/api/v1/admin/colaboradores', async ({ request }) => {
        body = await request.json();
        key = request.headers.get('Idempotency-Key');
        return HttpResponse.json({ empleado: { id: 'emp-1', nombre: 'Ana' }, acceso: null }, { status: 201 });
      }),
    );
    const queryClient = createTestQueryClient();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    const { result } = renderHook(() => useAltaColaborador(), {
      wrapper: createQueryWrapper(queryClient),
    });
    result.current.mutate({
      idempotencyKey: 'alta-1',
      command: {
        id: '00000000-0000-0000-0000-000000000000',
        clave: 'E-1',
        nombre: 'Ana',
        sucursalId: 'suc-1',
        departamentoId: 'dep-1',
        puestoId: 'pue-1',
        acceso: 0,
        correoCorporativo: null,
        emailContacto: null,
        rolId: null,
        jefeDirectoId: null,
        codigoNomina: null,
      },
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(key).toBe('alta-1');
    expect(body).toMatchObject({ clave: 'E-1', sucursalId: 'suc-1', acceso: 0 });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: adminKeys.empleados() });
  });
});
