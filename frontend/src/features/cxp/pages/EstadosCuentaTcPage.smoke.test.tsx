import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { EstadosCuentaTcPage } from '@/features/cxp/pages/EstadosCuentaTcPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EstadoCuentaTcStatus } from '@/features/cxp/api/types';

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
      'cuentas_por_pagar.tc.leer',
      'cuentas_por_pagar.tc.registrar-movimiento',
      'cuentas_por_pagar.tc.cerrar-estado-cuenta',
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

describe('<EstadosCuentaTcPage> — smoke', () => {
  it('estado empty muestra mensaje', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/estados-cuenta-tc', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<EstadosCuentaTcPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/Sin estados de cuenta/i),
      ).toBeInTheDocument(),
    );
    expect(
      screen.getByRole('button', { name: /Nuevo estado de cuenta/i }),
    ).toBeInTheDocument();
  });

  it('EC EnConciliacion sin archivo: muestra solo "Subir archivo"', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/estados-cuenta-tc', () =>
        HttpResponse.json({
          items: [
            {
              id: 'ec-1',
              tarjetaId: 't-1',
              periodoDesde: '2026-05-01',
              periodoHasta: '2026-05-31',
              fechaCorte: '2026-05-31',
              fechaLimitePago: '2026-06-15',
              estado: EstadoCuentaTcStatus.EnConciliacion,
              totalBancoMxn: null,
              totalConciliadoMxn: null,
              diferenciaMxn: null,
              perfilParserUsado: null,
              lineasCount: 0,
              version: 1,
            },
          ],
          offset: 0,
          limit: 200,
          total: 1,
        }),
      ),
    );
    render(<EstadosCuentaTcPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: /Subir archivo/i }),
      ).toBeInTheDocument(),
    );
    // Sin perfil parser → no muestra Conciliar auto todavía.
    expect(
      screen.queryByRole('button', { name: /Conciliar auto/i }),
    ).not.toBeInTheDocument();
  });

  it('axe-core: cero violations en estado vacío', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/estados-cuenta-tc', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    const { container } = render(<EstadosCuentaTcPage />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() =>
      expect(
        screen.getByText(/Sin estados de cuenta/i),
      ).toBeInTheDocument(),
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
