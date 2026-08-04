import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ViaticosPage } from '@/features/cxp/pages/ViaticosPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EstadoSolicitudViaticos } from '@/features/cxp/api/types';

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
      'cuentas_por_pagar.viaticos.leer',
      'cuentas_por_pagar.viaticos.solicitar',
      'cuentas_por_pagar.viaticos.autorizar-jefe',
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

describe('<ViaticosPage> — smoke', () => {
  it('estado empty muestra mensaje + botón Solicitar', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/viaticos', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<ViaticosPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin solicitudes/i)).toBeInTheDocument(),
    );
    expect(
      screen.getByRole('button', { name: /Solicitar viáticos/i }),
    ).toBeInTheDocument();
  });

  it('solicitud excede política muestra botón Aut. jefe', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/viaticos', () =>
        HttpResponse.json({
          items: [
            {
              id: 'v-1',
              empleadoId: 'e-1',
              jefeDirectoId: 'j-1',
              destino: 'CDMX',
              fechaSalida: '2026-06-01',
              fechaRegreso: '2026-06-03',
              montoSolicitado: 10000,
              topePolitica: 8000,
              excedePolitica: true,
              estado: EstadoSolicitudViaticos.Solicitada,
              montoComprobado: null,
              diferenciaLiquidacion: null,
              fechaSolicitud: '2026-05-24T00:00:00Z',
              version: 1,
            },
          ],
          offset: 0,
          limit: 200,
          total: 1,
        }),
      ),
    );
    render(<ViaticosPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: /Aut\. jefe/i }),
      ).toBeInTheDocument(),
    );
    expect(screen.getByText(/Pasará a DF/i)).toBeInTheDocument();
  });

  it('axe-core: cero violations en estado vacío', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/viaticos', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    const { container } = render(<ViaticosPage />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() =>
      expect(screen.getByText(/Sin solicitudes/i)).toBeInTheDocument(),
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
