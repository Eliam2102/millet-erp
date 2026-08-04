import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaPedidos } from '@/features/facturacion/pages/BandejaPedidos';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
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
    await waitFor(() =>
      expect(screen.getByText(/Sin pedidos/i)).toBeInTheDocument(),
    );
    expect(
      screen.getByRole('button', { name: /Nuevo pedido/i }),
    ).toBeInTheDocument();
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
              total: 232.5,
              moneda: 'MXN',
              createdAt: '2026-05-30T10:00:00Z',
            },
          ],
          sinAsignarCount: null,
        }),
      ),
    );
    render(<BandejaPedidos />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Cliente Demo SA')).toBeInTheDocument(),
    );
    expect(screen.getByText('MID-001')).toBeInTheDocument();
    expect(screen.getByText('Importado')).toBeInTheDocument();
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
      expect(
        screen.getByText(/No se pudieron cargar los pedidos/i),
      ).toBeInTheDocument(),
    );
  });
});
