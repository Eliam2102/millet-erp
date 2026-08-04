import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { NotasCreditoPage } from '@/features/cxp/pages/NotasCreditoPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  EstadoNotaCredito,
  TipoNotaCredito,
  TipoRelacionCfdi,
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
      'cuentas_por_pagar.notas-credito.leer',
      'cuentas_por_pagar.notas-credito.capturar',
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

describe('<NotasCreditoPage> — smoke', () => {
  it('estado empty muestra mensaje y botón captura', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/notas-credito', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<NotasCreditoPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin notas de crédito/i)).toBeInTheDocument(),
    );
    expect(
      screen.getByRole('button', { name: /Capturar NC/i }),
    ).toBeInTheDocument();
  });

  it('estado con datos renderiza NC EnEspera con UUID abreviado', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/notas-credito', () =>
        HttpResponse.json({
          items: [
            {
              id: 'nc-1',
              uuidCfdi: '11111111-2222-3333-4444-555555555555',
              proveedorId: 'p-1',
              folioProveedor: '888',
              serieProveedor: 'NC',
              fechaCfdi: '2026-05-20T00:00:00Z',
              total: 500,
              moneda: 'MXN',
              tipo: TipoNotaCredito.Descuento,
              tipoRelacionCfdi: TipoRelacionCfdi.NotaCredito,
              uuidRelacionCfdi: '99999999-9999-9999-9999-999999999999',
              facturaOrigenId: null,
              saldoPorAplicar: 500,
              estado: EstadoNotaCredito.EnEspera,
              version: 1,
            },
          ],
          offset: 0,
          limit: 200,
          total: 1,
        }),
      ),
    );
    render(<NotasCreditoPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/NC-888/i)).toBeInTheDocument(),
    );
    // Badge "En espera" — usar exact match para distinguir del párrafo descriptivo
    expect(screen.getByText(/^En espera$/i)).toBeInTheDocument();
  });

  it('axe-core: cero violations en estado vacío', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/notas-credito', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    const { container } = render(<NotasCreditoPage />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() =>
      expect(screen.getByText(/Sin notas de crédito/i)).toBeInTheDocument(),
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
