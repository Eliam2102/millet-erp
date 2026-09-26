import { describe, expect, it } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useEmpleadosAdmin } from '@/modules/administracion/api/empleados';
import { useAuthStore } from '@/lib/auth/auth-store';

/**
 * B2 (01-05): <c>useEmpleadosAdmin</c> es el consumidor exemplar de la
 * sucursal activa (<c>currentSucursalId</c>) — la incluye en la
 * queryKey y en el querystring de <c>GET /catalogos/empleados</c>. Al
 * cambiar de sucursal, TanStack Query trata la nueva combinación como
 * una query distinta y vuelve a pedir el catálogo con el filtro nuevo
 * (sin mezclar los registros de la sucursal anterior).
 */
describe('useEmpleadosAdmin — filtro por sucursal activa', () => {
  it('al cambiar la sucursal activa vuelve a pedir el catálogo con el nuevo filtro', async () => {
    useAuthStore.setState({ currentSucursalId: 'suc-1' });

    const urlsVistas: string[] = [];
    mswServer.use(
      http.get('*/api/v1/catalogos/empleados', ({ request }) => {
        const url = new URL(request.url);
        urlsVistas.push(url.toString());
        const sucursalId = url.searchParams.get('sucursalId');
        const items =
          sucursalId === 'suc-2'
            ? [{ id: 'emp-2', empresaId: 'e-1', clave: 'EMP-002', nombre: 'Pedro López', email: null, puestoId: null, jefeDirectoId: null, sucursalId: 'suc-2', departamentoId: null, usuarioId: null, estatus: 0 }]
            : [{ id: 'emp-1', empresaId: 'e-1', clave: 'EMP-001', nombre: 'Juana Pérez', email: null, puestoId: null, jefeDirectoId: null, sucursalId: 'suc-1', departamentoId: null, usuarioId: null, estatus: 0 }];
        return HttpResponse.json({ items, offset: 0, limit: 200, total: items.length });
      }),
    );

    const { result } = renderHook(() => useEmpleadosAdmin(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toHaveLength(1);
    expect(result.current.data?.[0].clave).toBe('EMP-001');
    expect(urlsVistas.at(-1)).toContain('sucursalId=suc-1');

    act(() => {
      useAuthStore.setState({ currentSucursalId: 'suc-2' });
    });

    await waitFor(() => expect(result.current.data?.[0].clave).toBe('EMP-002'));
    expect(urlsVistas.at(-1)).toContain('sucursalId=suc-2');
    // No se mezclan los registros de la sucursal anterior.
    expect(result.current.data).toHaveLength(1);

    useAuthStore.setState({ currentSucursalId: null });
  });
});
