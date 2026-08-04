import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ReporteAntiguedadSaldosPage } from '@/features/cxp/pages/ReporteAntiguedadSaldosPage';
import { useAuthStore } from '@/lib/auth/auth-store';

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
    permisos: ['cuentas_por_pagar.reportes.antiguedad'],
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

describe('<ReporteAntiguedadSaldosPage> — smoke', () => {
  it('estado inicial muestra filtros y mensaje "captura filtros"', async () => {
    render(<ReporteAntiguedadSaldosPage />, { wrapper: createQueryWrapper() });
    expect(
      screen.getByRole('button', { name: /Ejecutar/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Captura los filtros y ejecuta el reporte/i),
    ).toBeInTheDocument();
  });

  it('al ejecutar renderiza título + columnas + datos del backend', async () => {
    mswServer.use(
      http.get(
        '*/api/v1/cuentas-por-pagar/reportes/antiguedad-saldos',
        () =>
          HttpResponse.json({
            titulo: 'Antigüedad de saldos por proveedor',
            generadoEn: '2026-05-24T00:00:00Z',
            filtrosAplicados: [{ label: 'Fecha corte', valor: '2026-05-24' }],
            columnas: [
              { key: 'proveedor', label: 'Proveedor', tipo: 1, alineacion: 1 },
              { key: 'b0_30', label: '0-30 días', tipo: 4, alineacion: 3 },
              { key: 'bMas90', label: '+90 días', tipo: 4, alineacion: 3 },
            ],
            filas: [
              { proveedor: 'ABC SA', b0_30: 1000, bMas90: 0 },
              { proveedor: 'XYZ SA', b0_30: 500, bMas90: 2000 },
            ],
            totales: { b0_30: 1500, bMas90: 2000 },
          }),
      ),
    );
    render(<ReporteAntiguedadSaldosPage />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: /Ejecutar/i }));
    await waitFor(() =>
      expect(
        screen.getByRole('heading', {
          name: /Antigüedad de saldos por proveedor/i,
        }),
      ).toBeInTheDocument(),
    );
    expect(screen.getByText(/ABC SA/i)).toBeInTheDocument();
    expect(screen.getByText(/XYZ SA/i)).toBeInTheDocument();
  });

  it('axe-core: cero violations en estado inicial', async () => {
    const { container } = render(<ReporteAntiguedadSaldosPage />, {
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
