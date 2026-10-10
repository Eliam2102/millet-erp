import { renderHook, waitFor } from '@testing-library/react';
import { beforeEach, expect, it } from 'vitest';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useAdminPendientes } from './use-admin-pendientes';

beforeEach(() =>
  useAuthStore.setState({
    currentEmpresaId: 'empresa-prueba',
    currentSucursalId: 'sucursal-prueba',
    permisos: [],
  }),
);
it('no consulta endpoints sin sus permisos ni presenta errores como faltantes', async () => {
  const { result } = renderHook(() => useAdminPendientes(), { wrapper: createQueryWrapper() });
  expect(result.current.pendientes).toEqual([]);
  expect(result.current.cargando).toBe(false);
  expect(result.current.error).toBe(false);
});
it('combina respuestas del API con permisos, empresa y sucursal; consultas de series acotadas', async () => {
  const paths: URL[] = [];
  useAuthStore.setState({
    permisos: [P.CentrosCostoCatalogoLeer, P.AdminSeriesGestionar, P.AdminParametrosLeer],
  });
  mswServer.use(
    http.get('*/api/v1/centros-costo/equivalencias/sucursal-prueba', () =>
      HttpResponse.json([{ observaciones: 'DEMO · muestra de prueba' }]),
    ),
    http.get('*/api/v1/admin/series', ({ request }) => {
      paths.push(new URL(request.url));
      return HttpResponse.json({ items: [], total: 0 });
    }),
    http.get('*/api/v1/admin/parametros', () => HttpResponse.json({ items: [] })),
  );
  const { result } = renderHook(() => useAdminPendientes(), { wrapper: createQueryWrapper() });
  await waitFor(() => expect(result.current.pendientes).toHaveLength(3));
  expect(paths[0].searchParams.get('empresaId')).toBe('empresa-prueba');
  expect(paths[0].searchParams.get('sucursalId')).toBe('sucursal-prueba');
  expect(paths[0].searchParams.get('limit')).toBe('1');
  expect(result.current.pendientes[0].estado).toBe('Datos de ejemplo');
});
it('fallos de API no generan estados de configuración faltante', async () => {
  useAuthStore.setState({ permisos: [P.AdminParametrosLeer] });
  mswServer.use(
    http.get('*/api/v1/admin/parametros', () =>
      HttpResponse.json({ title: 'No disponible' }, { status: 503 }),
    ),
  );
  const { result } = renderHook(() => useAdminPendientes(), { wrapper: createQueryWrapper() });
  await waitFor(() => expect(result.current.error).toBe(true));
  expect(result.current.pendientes).toEqual([]);
});
it('sin sucursal no hace comprobaciones de series ni equivalencias', () => {
  useAuthStore.setState({
    currentSucursalId: null,
    permisos: [P.CentrosCostoCatalogoLeer, P.AdminSeriesGestionar],
  });
  const { result } = renderHook(() => useAdminPendientes(), { wrapper: createQueryWrapper() });
  expect(result.current.pendientes).toEqual([]);
  expect(result.current.requiereSucursal).toBe(true);
  expect(result.current.cargando).toBe(false);
});
