import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { createQueryWrapper } from '@/test/test-query-client';
import { CxpLandingPage } from '@/features/cxp/pages/CxpLandingPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

function setAuthWithPermisos(permisos: readonly string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: [...permisos],
    errorMessage: null,
  });
}

beforeEach(() => {
  setAuthWithPermisos([]);
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

describe('<CxpLandingPage> — smoke', () => {
  it('muestra el header del módulo', () => {
    render(<CxpLandingPage />, { wrapper: createQueryWrapper() });
    expect(
      screen.getByRole('heading', { name: /Cuentas por Pagar/i }),
    ).toBeInTheDocument();
  });

  it('sin permisos CxP renderiza mensaje neutro (no cards)', () => {
    render(<CxpLandingPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByTestId('cxp-sin-cards')).toBeInTheDocument();
    expect(
      screen.queryByText(/Antigüedad de saldos/i),
    ).not.toBeInTheDocument();
  });

  it('con permiso de facturas muestra card "Facturas"', () => {
    setAuthWithPermisos([PermisosCanonicos.CuentasPorPagarFacturasLeer]);
    render(<CxpLandingPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText(/^Facturas$/)).toBeInTheDocument();
    expect(screen.queryByTestId('cxp-sin-cards')).not.toBeInTheDocument();
  });

  it('con permiso de reportes muestra sección Reportes y card', () => {
    setAuthWithPermisos([
      PermisosCanonicos.CuentasPorPagarReportesAntiguedad,
    ]);
    render(<CxpLandingPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText(/Antigüedad de saldos/i)).toBeInTheDocument();
    expect(screen.getByText(/^Reportes$/)).toBeInTheDocument();
  });

  it('axe-core: cero violations con cards visibles', async () => {
    setAuthWithPermisos([
      PermisosCanonicos.CuentasPorPagarFacturasLeer,
      PermisosCanonicos.CuentasPorPagarCfdisLeer,
      PermisosCanonicos.CuentasPorPagarReportesAntiguedad,
    ]);
    const { container } = render(<CxpLandingPage />, {
      wrapper: createQueryWrapper(),
    });
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
