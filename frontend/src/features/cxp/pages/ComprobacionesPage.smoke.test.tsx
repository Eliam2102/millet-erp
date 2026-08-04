import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ComprobacionesPage } from '@/features/cxp/pages/ComprobacionesPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  EstadoComprobacionGastos,
  TipoComprobacionGastos,
} from '@/features/cxp/api/types';

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
      'cuentas_por_pagar.comprobaciones.leer',
      'cuentas_por_pagar.comprobaciones.capturar',
      'cuentas_por_pagar.comprobaciones.aprobar-nivel1',
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

describe('<ComprobacionesPage> — smoke', () => {
  it('estado empty muestra mensaje + botón Nueva comprobación', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/comprobaciones', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<ComprobacionesPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin comprobaciones/i)).toBeInTheDocument(),
    );
    expect(
      screen.getByRole('button', { name: /Nueva comprobación/i }),
    ).toBeInTheDocument();
  });

  it('estado con aduanal renderiza tipo y firma N1 disponible', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/comprobaciones', () =>
        HttpResponse.json({
          items: [
            {
              id: 'c-1',
              tipo: TipoComprobacionGastos.GastosAduanales,
              estado: EstadoComprobacionGastos.PorRevisar,
              sucursalId: 's-1',
              responsableId: 'r-1',
              fechaInicio: '2026-05-01',
              fechaFin: '2026-05-15',
              montoTotal: 50000,
              moneda: 'MXN',
              numeroLineas: 3,
              fechaCreacion: '2026-05-20T00:00:00Z',
              version: 1,
            },
          ],
          offset: 0,
          limit: 200,
          total: 1,
        }),
      ),
    );
    render(<ComprobacionesPage />, { wrapper: createQueryWrapper() });
    // Esperar a que el botón de la fila aparezca (señal segura de que
    // los items se cargaron del mock — Aduanales aparece también en la
    // descripción del header así que no sirve como señal).
    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: /Firma N1/i }),
      ).toBeInTheDocument(),
    );
  });

  it('axe-core: cero violations en estado vacío', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/comprobaciones', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    const { container } = render(<ComprobacionesPage />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() =>
      expect(screen.getByText(/Sin comprobaciones/i)).toBeInTheDocument(),
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
