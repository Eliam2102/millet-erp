import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { AprobadoresLimitesPage } from '@/features/cxp/pages/AprobadoresLimitesPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { TipoGastoAprobador } from '@/features/cxp/api/types';

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
    permisos: ['cuentas_por_pagar.catalogos.aprobadores.administrar'],
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

describe('<AprobadoresLimitesPage> — smoke', () => {
  it('estado empty + botón Nuevo aprobador', async () => {
    mswServer.use(
      http.get(
        '*/api/v1/cuentas-por-pagar/catalogos/aprobadores-limites',
        () =>
          HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<AprobadoresLimitesPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin aprobadores/i)).toBeInTheDocument(),
    );
    expect(
      screen.getByRole('button', { name: /Nuevo aprobador/i }),
    ).toBeInTheDocument();
  });

  it('aprobador vigente con permiso muestra botón Cerrar', async () => {
    mswServer.use(
      http.get(
        '*/api/v1/cuentas-por-pagar/catalogos/aprobadores-limites',
        () =>
          HttpResponse.json({
            items: [
              {
                id: 'a-1',
                empleadoId: 'e-1234567890ab-1234-1234567890ab',
                tipoGasto: TipoGastoAprobador.Viaticos,
                montoMax: 10000,
                moneda: 'MXN',
                vigenciaDesde: '2026-01-01',
                vigenciaHasta: null,
                version: 1,
              },
            ],
            offset: 0,
            limit: 200,
            total: 1,
          }),
      ),
    );
    render(<AprobadoresLimitesPage />, { wrapper: createQueryWrapper() });
    // Esperar a que el botón Cerrar de la fila aparezca (señal segura
    // de que los items se cargaron — "Viáticos" aparece también en el
    // filter dropdown como SelectItem).
    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: /Cerrar/i }),
      ).toBeInTheDocument(),
    );
  });

  it('axe-core: cero violations en estado vacío', async () => {
    mswServer.use(
      http.get(
        '*/api/v1/cuentas-por-pagar/catalogos/aprobadores-limites',
        () =>
          HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    const { container } = render(<AprobadoresLimitesPage />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() =>
      expect(screen.getByText(/Sin aprobadores/i)).toBeInTheDocument(),
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
