import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaPedidos } from '@/features/facturacion/pages/BandejaPedidos';
import { useAuthStore } from '@/lib/auth/auth-store';

// F6.1: search params mutables para probar el filtro «Clase».
const routerMocks = vi.hoisted(() => ({
  search: {} as Record<string, unknown>,
}));

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => routerMocks.search,
}));

// El botón "Nuevo pedido" consume el provider shell-level; en el test
// lo reemplazamos por un noop para no montar todo el shell.
const abrirSpy = vi.fn();
vi.mock('@/features/facturacion/components/nuevo-pedido-context', () => ({
  useNuevoPedido: () => ({ abrir: abrirSpy, cerrar: () => {}, setDirty: () => {} }),
}));

beforeEach(() => {
  abrirSpy.mockClear();
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['facturacion.pedidos.capturar', 'facturacion.facturas.leer'],
    errorMessage: null,
  });
});

afterEach(() => {
  routerMocks.search = {};
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

describe('<BandejaPedidos> — smoke', () => {
  it('estado empty + botón Nuevo pedido visible con permiso', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables', () =>
        HttpResponse.json({ items: [], sinAsignarCount: null }),
      ),
    );
    render(<BandejaPedidos />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText(/Sin pedidos/i)).toBeInTheDocument());
    expect(screen.getByRole('button', { name: /Nuevo pedido/i })).toBeInTheDocument();
  });

  it('renderiza un pedido con su estado y total', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables', () =>
        HttpResponse.json({
          items: [
            {
              id: 'p-1',
              numeroPedido: 'MID-001',
              origen: 'Manual',
              estado: 'Importado',
              clienteNombre: 'Cliente Demo SA',
              total: 1000,
              totalConIva: 1160,
              moneda: 'MXN',
              createdAt: '2026-05-30T10:00:00Z',
            },
            {
              id: 'p-2',
              numeroPedido: 'MID-002',
              origen: 'Aw',
              estado: 'Importado',
              clienteNombre: 'Cliente Legacy SA',
              total: 50,
              totalConIva: null,
              moneda: 'MXN',
              createdAt: '2026-05-29T10:00:00Z',
            },
          ],
          sinAsignarCount: null,
        }),
      ),
    );
    render(<BandejaPedidos />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('Cliente Demo SA')).toBeInTheDocument());
    expect(screen.getByText('MID-001')).toBeInTheDocument();
    expect(screen.getAllByRole('cell', { name: 'Importado' })).toHaveLength(2);
    // 86e3tfd7j: columna Total con IVA (caja ve cuánto cobrará).
    expect(screen.getByRole('columnheader', { name: 'Total con IVA' })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: /1,160\.00/ })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: '—' })).toHaveAttribute(
      'title',
      'Sin tasa de IVA en alguna línea',
    );
  });

  it('F6.1: badges de clase y filtro «Clase» sincronizado con ?clase=', async () => {
    routerMocks.search = { clase: 'VentaDirecta' };
    let urlRecibida = '';
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables', ({ request }) => {
        urlRecibida = request.url;
        return HttpResponse.json({
          items: [
            {
              id: 'p-vd',
              numeroPedido: null,
              origen: 'Manual',
              estado: 'Importado',
              clienteNombre: 'Cliente VD',
              total: 10,
              moneda: 'MXN',
              createdAt: '2026-10-19T10:00:00Z',
              claseManual: 'VentaDirecta',
              subtipoVentaDirecta: 'Servicio',
              esEmergencia: false,
            },
            {
              id: 'p-aw',
              numeroPedido: 'AW-1',
              origen: 'Aw',
              estado: 'Importado',
              clienteNombre: 'Cliente A+W',
              total: 20,
              moneda: 'MXN',
              createdAt: '2026-10-19T10:00:00Z',
              claseManual: null,
              subtipoVentaDirecta: null,
              esEmergencia: false,
            },
          ],
          sinAsignarCount: null,
        });
      }),
    );
    render(<BandejaPedidos />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Cliente VD')).toBeInTheDocument(),
    );
    expect(new URL(urlRecibida).searchParams.get('clase')).toBe('VentaDirecta');
    // Solo el pedido manual clasificado lleva badge de clase.
    const badges = screen.getAllByTestId('clase-pedido-badge');
    expect(badges.map((b) => b.textContent)).toEqual(['Venta directa · Servicio']);
    expect(screen.getByRole('combobox', { name: /Filtrar por clase/i })).toHaveTextContent(
      'Venta directa',
    );
    // Hay filtro activo → aparece «Limpiar».
    expect(screen.getByRole('button', { name: /Limpiar/i })).toBeInTheDocument();
  });

  it('estado error con retry', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/pedidos-facturables', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Boom', status: 500 },
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<BandejaPedidos />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/No se pudieron cargar los pedidos/i)).toBeInTheDocument(),
    );
  });
});
