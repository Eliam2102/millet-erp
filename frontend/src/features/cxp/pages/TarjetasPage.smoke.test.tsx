import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { TarjetasPage } from '@/features/cxp/pages/TarjetasPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EstadoTarjeta } from '@/features/cxp/api/types';

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
      'cuentas_por_pagar.tc.administrar',
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

describe('<TarjetasPage> — smoke', () => {
  it('estado empty muestra mensaje + botón Nueva tarjeta', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/tarjetas', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<TarjetasPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin tarjetas/i)).toBeInTheDocument(),
    );
    expect(
      screen.getByRole('button', { name: /Nueva tarjeta/i }),
    ).toBeInTheDocument();
  });

  it('tarjeta activa muestra botón Bloquear y Cancelar', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/tarjetas', () =>
        HttpResponse.json({
          items: [
            {
              id: 't-1',
              emisora: 'Visa',
              perfilParser: 'BBVA_2024',
              numeroEnmascarado: '**** **** **** 1234',
              nombreAlias: 'TC Operaciones',
              titularId: 't-emp',
              bancoProveedorId: 'b-1',
              limiteCreditoMxn: 50000,
              monedaDefault: 'MXN',
              diaCorte: 15,
              diaLimitePago: 5,
              estado: EstadoTarjeta.Activa,
              fechaBloqueo: null,
              motivoBloqueo: null,
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
    render(<TarjetasPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/TC Operaciones/i)).toBeInTheDocument(),
    );
    expect(
      screen.getByRole('button', { name: /Bloquear/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /^Cancelar$/i }),
    ).toBeInTheDocument();
  });

  it('axe-core: cero violations en estado vacío', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/tarjetas', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    const { container } = render(<TarjetasPage />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() =>
      expect(screen.getByText(/Sin tarjetas/i)).toBeInTheDocument(),
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
