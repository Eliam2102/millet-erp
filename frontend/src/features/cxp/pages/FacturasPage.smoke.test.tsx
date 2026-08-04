import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { FacturasPage } from '@/features/cxp/pages/FacturasPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EstadoPasivo } from '@/features/cxp/api/types';

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
      'cuentas_por_pagar.facturas.leer',
      'cuentas_por_pagar.facturas.capturar',
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

describe('<FacturasPage> — smoke', () => {
  it('estado loading muestra el header', () => {
    mswServer.use(
      http.get(
        '*/api/v1/cuentas-por-pagar/facturas',
        () => new Promise(() => {}),
      ),
    );
    render(<FacturasPage />, { wrapper: createQueryWrapper() });
    expect(
      screen.getByRole('heading', { name: /^Facturas$/i }),
    ).toBeInTheDocument();
  });

  it('estado empty muestra mensaje y botón "Capturar factura"', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/facturas', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<FacturasPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin facturas/i)).toBeInTheDocument(),
    );
    expect(
      screen.getByRole('button', { name: /Capturar factura/i }),
    ).toBeInTheDocument();
  });

  it('estado con datos renderiza folios y estado', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/facturas', () =>
        HttpResponse.json({
          items: [
            {
              id: 'f-1',
              proveedorId: 'p-1',
              sucursalId: 's-1',
              folioProveedor: '999',
              serieProveedor: 'A',
              fechaDocumento: '2026-05-15T00:00:00Z',
              fechaVencimiento: '2026-06-15',
              total: 1234.56,
              saldoPendiente: 1234.56,
              moneda: 'MXN',
              estado: EstadoPasivo.Capturada,
              ordenCompraId: null,
              version: 1,
            },
          ],
          offset: 0,
          limit: 200,
          total: 1,
        }),
      ),
    );
    render(<FacturasPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/A-999/i)).toBeInTheDocument(),
    );
    expect(screen.getByText(/^Capturada$/i)).toBeInTheDocument();
  });

  it('axe-core: cero violations en estado vacío', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/facturas', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    const { container } = render(<FacturasPage />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() =>
      expect(screen.getByText(/Sin facturas/i)).toBeInTheDocument(),
    );
    const results: AxeResults = await axe.run(container, {
      runOnly: {
        type: 'tag',
        values: ['wcag2a', 'wcag2aa', 'wcag21aa'],
      },
    });
    if (results.violations.length > 0) {
      const detalle = results.violations
        .map((v) => `${v.id} (${v.impact}): ${v.description}`)
        .join('\n');
      throw new Error(`axe-core violations:\n${detalle}`);
    }
    expect(results.violations).toHaveLength(0);
  });
});
