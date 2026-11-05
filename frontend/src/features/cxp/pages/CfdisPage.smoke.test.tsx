import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { CfdisPage } from '@/features/cxp/pages/CfdisPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  CanalOrigenCfdi,
  EstadoCfdiRecibido,
  TipoCfdi,
} from '@/features/cxp/api/types';

let searchMock: Record<string, unknown> = {};
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => searchMock,
}));

beforeEach(() => {
  // Los hijos de la página consultan estos endpoints; sin handler la petición
  // sale a la red real y su 401 tardío limpiaría la sesión del test siguiente.
  mswServer.use(
    http.get('*/api/v1/cuentas-por-pagar/cfdis/buzon/revision', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
    ),
    http.get('*/api/v1/cuentas-por-pagar/cfdis/buzon/ejecuciones', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 1, total: 0 }),
    ),
    http.get('*/api/v1/catalogos/sucursales', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
    http.get('*/api/v1/catalogos/condiciones-pago', () => HttpResponse.json([])),
    );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: [
      'cuentas_por_pagar.cfdis.leer',
      'cuentas_por_pagar.cfdis.descartar',
      'cuentas_por_pagar.cfdis.cargar-manual',
    ],
    errorMessage: null,
  });
});

afterEach(() => {
  searchMock = {};
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

describe('<CfdisPage> — smoke', () => {
  it('estado loading muestra el header', () => {
    mswServer.use(
      http.get(
        '*/api/v1/cuentas-por-pagar/cfdis',
        () => new Promise(() => {}),
      ),
    );
    render(<CfdisPage />, { wrapper: createQueryWrapper() });
    expect(
      screen.getByRole('heading', { name: /CFDIs recibidos/i }),
    ).toBeInTheDocument();
  });

  it('estado empty muestra mensaje y filtros disponibles', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/cfdis', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<CfdisPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin CFDIs/i)).toBeInTheDocument(),
    );
    expect(screen.getByLabelText(/Buscar por UUID o folio/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Filtrar por estado/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Filtrar por tipo/i)).toBeInTheDocument();
    expect(
      screen.getByLabelText(/Filtrar por RFC emisor/i),
    ).toBeInTheDocument();
    expect(
      screen.getByLabelText(/Solo por procesar > 5 días/i),
    ).toBeInTheDocument();
  });

  it('busca y pagina en el servidor (q, offset, limit) y muestra el total', async () => {
    searchMock = { q: 'F3A9', page: 2 };
    let url = '';
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/cfdis', ({ request }) => {
        url = request.url;
        return HttpResponse.json({ items: [], offset: 25, limit: 25, total: 0 });
      }),
    );
    render(<CfdisPage />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(url).toContain('q=F3A9'));
    expect(url).toContain('offset=25');
    expect(url).toContain('limit=25');
  });

  it('estado con datos renderiza UUIDs y RFC', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/cfdis', () =>
        HttpResponse.json({
          items: [
            {
              id: 'c-1',
              uuidCfdi: '11111111-2222-3333-4444-555555555555',
              rfcEmisor: 'ABC010101AB1',
              tipo: TipoCfdi.Ingreso,
              folio: '12345',
              serie: 'A',
              fechaCfdi: '2026-05-15T00:00:00Z',
              total: 1234.56,
              moneda: 'MXN',
              canalOrigen: CanalOrigenCfdi.CargaManual,
              fechaRecepcion: '2026-05-20T10:00:00Z',
              estado: EstadoCfdiRecibido.PorProcesar,
            },
          ],
          offset: 0,
          limit: 200,
          total: 1,
        }),
      ),
    );
    render(<CfdisPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/ABC010101AB1/i)).toBeInTheDocument(),
    );
    // Badge "Por procesar" (no el label del checkbox "Solo por procesar...")
    expect(screen.getByText(/^Por procesar$/i)).toBeInTheDocument();
    expect(screen.getByText(/A-12345/i)).toBeInTheDocument();
    expect(screen.getByText('1–1 de 1')).toBeInTheDocument();
  });

  it('muestra la columna Estatus SAT con vigente, cancelado y sin dato', async () => {
    const base = {
      rfcEmisor: 'ABC010101AB1',
      tipo: TipoCfdi.Ingreso,
      folio: '1',
      serie: 'A',
      fechaCfdi: '2026-05-15T00:00:00Z',
      total: 10,
      moneda: 'MXN',
      canalOrigen: CanalOrigenCfdi.CargaManual,
      fechaRecepcion: '2026-05-20T10:00:00Z',
      estado: EstadoCfdiRecibido.PorProcesar,
    };
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/cfdis', () =>
        HttpResponse.json({
          items: [
            { ...base, id: 'c-1', uuidCfdi: '11111111-2222-3333-4444-555555555551', estatusSat: 1 },
            {
              ...base,
              id: 'c-2',
              uuidCfdi: '11111111-2222-3333-4444-555555555552',
              estatusSat: 2,
              fechaCancelacionSat: '2026-11-02T12:00:00Z',
            },
            { ...base, id: 'c-3', uuidCfdi: '11111111-2222-3333-4444-555555555553', estatusSat: null },
            { ...base, id: 'c-4', uuidCfdi: '11111111-2222-3333-4444-555555555554' },
          ],
          offset: 0,
          limit: 200,
          total: 4,
        }),
      ),
    );
    render(<CfdisPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getAllByText(/ABC010101AB1/i).length).toBe(4),
    );
    expect(screen.getAllByText('Estatus SAT').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Vigente').length).toBeGreaterThan(0);
    expect(screen.getAllByText(/Cancelado/).length).toBeGreaterThan(0);
    const filas = screen.getAllByRole('row').slice(1);
    const encabezado = screen.getByRole('columnheader', { name: 'Estatus SAT' });
    const columna = Array.from(encabezado.parentElement!.children).indexOf(encabezado);
    expect(filas[2].querySelectorAll('td')[columna]).toHaveTextContent(/^—$/);
    expect(filas[3].querySelectorAll('td')[columna]).toHaveTextContent(/^—$/);
    expect(screen.queryByText('Sin dato')).not.toBeInTheDocument();
  });

  it('muestra el botón "Cargar CFDI" con el permiso cargar-manual', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/cfdis', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<CfdisPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin CFDIs/i)).toBeInTheDocument(),
    );
    expect(
      screen.getByRole('button', { name: /Cargar CFDI/i }),
    ).toBeInTheDocument();
  });

  it('oculta el botón "Cargar CFDI" sin el permiso cargar-manual', async () => {
    useAuthStore.setState({
      permisos: [
        'cuentas_por_pagar.cfdis.leer',
        'cuentas_por_pagar.cfdis.descartar',
      ],
    });
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/cfdis', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<CfdisPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin CFDIs/i)).toBeInTheDocument(),
    );
    expect(
      screen.queryByRole('button', { name: /Cargar CFDI/i }),
    ).not.toBeInTheDocument();
  });

  it('axe-core: cero violations en estado vacío', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/cfdis', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    const { container } = render(<CfdisPage />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() =>
      expect(screen.getByText(/Sin CFDIs/i)).toBeInTheDocument(),
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
