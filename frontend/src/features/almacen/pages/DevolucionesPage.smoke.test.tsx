import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DevolucionesPage } from '@/features/almacen/pages/DevolucionesPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EstadoDevolucionProveedor } from '@/features/almacen/api/types';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

// Sheets pesados fuera de foco del smoke (la bandeja es lo que se prueba).
vi.mock('@/features/almacen/components/NuevaDevolucionProveedorSheet', () => ({
  NuevaDevolucionProveedorSheet: () => null,
}));
vi.mock('@/features/almacen/components/AplicarDevolucionInternaSheet', () => ({
  AplicarDevolucionInternaSheet: () => null,
}));
vi.mock('@/features/almacen/components/MatRevSheet', () => ({
  MatRevSheet: () => null,
}));

function devolucion(partial: Record<string, unknown>) {
  return {
    id: 'd-1',
    proveedorId: '019e5ba8-41b2-7ee8-8f40-4435f4808968',
    estado: EstadoDevolucionProveedor.Borrador,
    montoTotalMxn: 100,
    solicitadaAt: '2026-07-10T12:00:00Z',
    autorizadaAt: null,
    registradaAt: null,
    conciliadaConNcFiscalAt: null,
    folioMovimientoSalida: null,
    proveedorNombre: null,
    ...partial,
  };
}

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['almacen.devoluciones.leer'],
    errorMessage: null,
  });
});

afterEach(() => {
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
    errorMessage: null,
  });
});

describe('<DevolucionesPage> — smoke', () => {
  it('columna Proveedor muestra la razón social resuelta; cae al id truncado si no resuelve', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/devoluciones-proveedor', () =>
        HttpResponse.json({
          items: [
            devolucion({
              id: 'd-1',
              proveedorNombre: 'Vidrios del Norte SA',
            }),
            devolucion({
              id: 'd-2',
              proveedorNombre: null,
            }),
          ],
          offset: 0,
          limit: 200,
          total: 2,
        }),
      ),
    );
    render(<DevolucionesPage />, { wrapper: createQueryWrapper() });

    // Fila resuelta: razón social, no el GUID.
    await waitFor(() =>
      expect(screen.getByText('Vidrios del Norte SA')).toBeInTheDocument(),
    );
    // Fila sin resolver: id truncado (nunca el GUID completo).
    expect(screen.getByText('019e5ba8…')).toBeInTheDocument();
    expect(
      screen.queryByText('019e5ba8-41b2-7ee8-8f40-4435f4808968'),
    ).not.toBeInTheDocument();
  });

  it('estado empty muestra la bandeja vacía', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/devoluciones-proveedor', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<DevolucionesPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByRole('heading', { name: /Devoluciones a proveedor/i }),
      ).toBeInTheDocument(),
    );
  });
});
