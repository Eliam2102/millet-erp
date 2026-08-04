import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { RecepcionesPage } from '@/features/almacen/pages/RecepcionesPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EstadoMovimiento } from '@/features/almacen/api/types';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

function setupSubAlmacenes() {
  mswServer.use(
    http.get('*/api/v1/almacen/sub-almacenes', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 }),
    ),
  );
}

beforeEach(() => {
  setupSubAlmacenes();
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['almacen.entradas.leer', 'almacen.entradas.registrar'],
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

describe('<RecepcionesPage> — smoke', () => {
  it('estado loading muestra el header', () => {
    mswServer.use(
      http.get(
        '*/api/v1/almacen/recepciones',
        () => new Promise(() => {}),
      ),
    );
    render(<RecepcionesPage />, { wrapper: createQueryWrapper() });
    expect(
      screen.getByRole('heading', { name: /Recepciones/i }),
    ).toBeInTheDocument();
  });

  it('estado empty muestra mensaje y botón "Nueva recepción"', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/recepciones', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<RecepcionesPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin recepciones/i)).toBeInTheDocument(),
    );
    expect(
      screen.getByRole('button', { name: /Nueva recepción/i }),
    ).toBeInTheDocument();
  });

  it('estado con datos renderiza folios', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/recepciones', () =>
        HttpResponse.json({
          items: [
            {
              id: 'r-1',
              folio: 'EC2026-000001',
              fechaMovimiento: '2026-05-24',
              subAlmacenId: 's-1',
              ordenCompraId: 'oc-1',
              cfdiRecibidoId: null,
              montoTotalMxn: 12345.67,
              estado: EstadoMovimiento.Registrado,
            },
          ],
          offset: 0,
          limit: 200,
          total: 1,
        }),
      ),
    );
    render(<RecepcionesPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('EC2026-000001')).toBeInTheDocument(),
    );
  });

  it('columna OC muestra el folio resuelto; cae al id truncado si no hay folio', async () => {
    const OC_ID_SIN_FOLIO = '019e5ba8-41b2-7ee8-8f40-4435f4808968';
    mswServer.use(
      http.get('*/api/v1/almacen/recepciones', () =>
        HttpResponse.json({
          items: [
            {
              id: 'r-1',
              folio: 'M-ENT2026-000005',
              fechaMovimiento: '2026-05-24',
              subAlmacenId: 's-1',
              ordenCompraId: 'oc-1',
              ordenCompraFolio: 'OC-MID2026-000050',
              cfdiRecibidoId: null,
              montoTotalMxn: 100,
              estado: EstadoMovimiento.Registrado,
            },
            {
              id: 'r-2',
              folio: 'M-ENT2026-000006',
              fechaMovimiento: '2026-05-23',
              subAlmacenId: 's-1',
              ordenCompraId: OC_ID_SIN_FOLIO,
              ordenCompraFolio: null,
              cfdiRecibidoId: null,
              montoTotalMxn: 200,
              estado: EstadoMovimiento.Registrado,
            },
          ],
          offset: 0,
          limit: 200,
          total: 2,
        }),
      ),
    );
    render(<RecepcionesPage />, { wrapper: createQueryWrapper() });

    // Fila con folio: se pinta el folio humano, no el GUID.
    await waitFor(() =>
      expect(screen.getByText('OC-MID2026-000050')).toBeInTheDocument(),
    );
    // Fila sin folio: cae al id truncado (nunca el GUID completo).
    expect(screen.getByText('019e5ba8…')).toBeInTheDocument();
    expect(screen.queryByText(OC_ID_SIN_FOLIO)).not.toBeInTheDocument();
  });

  it('axe-core: cero violations en estado vacío', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/recepciones', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    const { container } = render(<RecepcionesPage />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() =>
      expect(screen.getByText(/Sin recepciones/i)).toBeInTheDocument(),
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
