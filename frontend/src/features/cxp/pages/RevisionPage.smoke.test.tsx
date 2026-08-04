import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { RevisionPage } from '@/features/cxp/pages/RevisionPage';
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
    permisos: ['cuentas_por_pagar.facturas.liberar-revision'],
    errorMessage: null,
  });
  // Catálogo de motivos siempre devuelve vacío en estos tests.
  mswServer.use(
    http.get(
      '*/api/v1/cuentas-por-pagar/catalogos/motivos-revision',
      () => HttpResponse.json([]),
    ),
  );
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

describe('<RevisionPage> — smoke', () => {
  it('sin dependenciaRevisoraId muestra empty state contextual', async () => {
    render(<RevisionPage />, { wrapper: createQueryWrapper() });
    expect(
      screen.getByRole('heading', { name: /Revisión por área/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Selecciona una dependencia/i),
    ).toBeInTheDocument();
  });

  it('axe-core: cero violations en estado sin dependencia', async () => {
    const { container } = render(<RevisionPage />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() =>
      expect(
        screen.getByText(/Selecciona una dependencia/i),
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
