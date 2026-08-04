import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { AnticiposPage } from '@/features/cxp/pages/AnticiposPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EstadoAnticipo } from '@/features/cxp/api/types';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: [
      'cuentas_por_pagar.anticipos.leer',
      'cuentas_por_pagar.anticipos.capturar',
    ],
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

describe('<AnticiposPage> — smoke', () => {
  it('estado empty + botón Capturar anticipo', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/anticipos', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<AnticiposPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin anticipos/i)).toBeInTheDocument(),
    );
    expect(
      screen.getByRole('button', { name: /Capturar anticipo/i }),
    ).toBeInTheDocument();
  });

  it('renderiza saldo amortizable destacado', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/anticipos', () =>
        HttpResponse.json({
          items: [
            {
              id: 'a-1',
              uuidCfdi: '11111111-2222-3333-4444-555555555555',
              proveedorId: 'p-1',
              serie: 'FANT',
              folioProveedor: '777',
              fechaCfdi: '2026-05-10T00:00:00Z',
              moneda: 'MXN',
              montoEntregado: 5000,
              montoAmortizado: 1500,
              saldoAmortizable: 3500,
              ordenCompraId: null,
              estado: EstadoAnticipo.Abierto,
              version: 1,
            },
          ],
          offset: 0,
          limit: 200,
          total: 1,
        }),
      ),
    );
    render(<AnticiposPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/FANT-777/i)).toBeInTheDocument(),
    );
    expect(screen.getByText(/^Abierto$/i)).toBeInTheDocument();
  });
});
