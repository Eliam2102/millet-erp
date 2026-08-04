import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SalidasPage } from '@/features/almacen/pages/SalidasPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EstadoMovimiento } from '@/features/almacen/api/types';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/almacen/sub-almacenes', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 }),
    ),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: [
      'almacen.salidas.leer-todas',
      'almacen.salidas.registrar',
      'almacen.salidas.por-vale',
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

describe('<SalidasPage> — smoke', () => {
  it('estado empty muestra "Sin salidas"', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/salidas', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<SalidasPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin salidas/i)).toBeInTheDocument(),
    );
  });

  it('estado con datos renderiza folio + badge Vale para vales', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/salidas', () =>
        HttpResponse.json({
          items: [
            {
              id: 's-1',
              folio: 'SC2026-000001',
              fechaMovimiento: '2026-05-24',
              subAlmacenId: 'sa-1',
              rqId: 'rq-1',
              esPorVale: false,
              personaDestinatariaId: null,
              montoTotalMxn: 1234.56,
              estado: EstadoMovimiento.Registrado,
              rqRegularizadoraId: null,
              rqFolio: 'RQ-MID2026-000010',
              rqRegularizadoraFolio: null,
            },
            {
              id: 's-2',
              folio: 'SV2026-000001',
              fechaMovimiento: '2026-05-24',
              subAlmacenId: 'sa-1',
              rqId: null,
              esPorVale: true,
              personaDestinatariaId: null,
              montoTotalMxn: 999.99,
              estado: EstadoMovimiento.Registrado,
              rqRegularizadoraId: null,
              rqFolio: null,
              rqRegularizadoraFolio: null,
            },
            {
              id: 's-3',
              folio: 'SV2026-000002',
              fechaMovimiento: '2026-05-23',
              subAlmacenId: 'sa-1',
              rqId: null,
              esPorVale: true,
              personaDestinatariaId: null,
              montoTotalMxn: 100,
              estado: EstadoMovimiento.Registrado,
              rqRegularizadoraId: 'rq-reg-1',
              rqFolio: null,
              rqRegularizadoraFolio: 'RQ-MID2026-000099',
            },
          ],
          offset: 0,
          limit: 200,
          total: 2,
        }),
      ),
    );
    render(<SalidasPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('SC2026-000001')).toBeInTheDocument(),
    );
    expect(screen.getByText('SV2026-000001')).toBeInTheDocument();
    // ValeBadge muestra "Vale (Xh)" / "Vale Xd!" / "Vale ✓" según
    // estado de regularización + antigüedad. Match parcial (con la
    // columna RQ ahora hay más de un "Vale …" en pantalla).
    expect(screen.getAllByText(/Vale[\s(✓]/).length).toBeGreaterThan(0);
    // Columna RQ — nunca UUID (ADR-0042): salida por RQ → folio directo;
    // vale regularizado → "Vale · {folio de la RQ regularizadora}"; vale
    // sin regularizar → "—" (fila s-2). Ningún id crudo en pantalla.
    expect(screen.getByText('RQ-MID2026-000010')).toBeInTheDocument();
    expect(screen.getByText(/Vale · RQ-MID2026-000099/)).toBeInTheDocument();
    expect(screen.getByText('—')).toBeInTheDocument();
    expect(screen.queryByText(/rq-1|rq-reg-1/)).not.toBeInTheDocument();
  });

  it('axe-core: cero violations en estado vacío', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/salidas', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    const { container } = render(<SalidasPage />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() =>
      expect(screen.getByText(/Sin salidas/i)).toBeInTheDocument(),
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
