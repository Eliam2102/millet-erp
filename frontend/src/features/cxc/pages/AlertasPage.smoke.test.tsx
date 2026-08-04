import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { TipoAlertaCartera } from '@/features/cxc/api/types';
import { useAuthStore } from '@/lib/auth/auth-store';

const searchMock = vi.hoisted(() => ({ current: {} as Record<string, unknown> }));

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => searchMock.current,
}));

import { AlertasPage } from '@/features/cxc/pages/AlertasPage';
import { CxcLandingPage } from '@/features/cxc/pages/CxcLandingPage';

const ALERTAS = '*/api/v1/cuentas-por-cobrar/alertas';
const ANTIGUEDAD = '*/api/v1/cuentas-por-cobrar/cartera/antiguedad';
const LOOKUP = '*/api/v1/cuentas-por-cobrar/clientes-lookup';

const alerta = {
  id: 'al-1',
  clienteId: 'cli-1',
  tipo: TipoAlertaCartera.ExcesoCredito,
  moneda: 'MXN',
  detalle: 'Saldo 520,000 excede el límite 500,000 de la línea MXN.',
  disparadaEn: '2026-07-14T06:00:00Z',
  atendida: false,
  atendidaPor: null,
  atendidaEn: null,
  version: 1,
};

beforeEach(() => {
  searchMock.current = {};
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: [
      'cuentas_por_cobrar.cartera.leer',
      'cuentas_por_cobrar.cobranza.registrar',
      'cuentas_por_cobrar.lineas-credito.leer',
    ],
    errorMessage: null,
  });
  mswServer.use(
    http.get(LOOKUP, () =>
      HttpResponse.json([
        { id: 'cli-1', clave: 'C001', rfc: 'AAA010101AAA', razonSocial: 'ACME SA' },
      ]),
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

describe('<AlertasPage> — smoke', () => {
  it('pendientes por default con botón Atender', async () => {
    mswServer.use(
      http.get(ALERTAS, ({ request }) => {
        const url = new URL(request.url);
        expect(url.searchParams.get('atendida')).toBe('false');
        return HttpResponse.json({
          items: [alerta],
          offset: 0,
          limit: 200,
          total: 1,
        });
      }),
    );
    render(<AlertasPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText(/Saldo 520,000 excede/i)).toBeInTheDocument();
    expect(await screen.findByText('ACME SA')).toBeInTheDocument();
    expect(screen.getByText('Exceso de crédito')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Atender/i })).toBeInTheDocument();
  });

  it('vacío: cartera al día', async () => {
    mswServer.use(
      http.get(ALERTAS, () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<AlertasPage />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText(/Sin alertas pendientes — cartera al día/i),
    ).toBeInTheDocument();
  });

  it('sin permiso registrar: sin botón Atender', async () => {
    useAuthStore.setState({
      permisos: [
        'cuentas_por_cobrar.cartera.leer',
        'cuentas_por_cobrar.lineas-credito.leer',
      ],
    });
    mswServer.use(
      http.get(ALERTAS, () =>
        HttpResponse.json({
          items: [alerta],
          offset: 0,
          limit: 200,
          total: 1,
        }),
      ),
    );
    render(<AlertasPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText(/Saldo 520,000/i)).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Atender/i }),
    ).not.toBeInTheDocument();
  });

  it('estado error con retry', async () => {
    mswServer.use(
      http.get(ALERTAS, () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Boom', status: 500 },
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<AlertasPage />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText(/No se pudieron cargar las alertas/i),
    ).toBeInTheDocument();
  });
});

describe('<CxcLandingPage> — indicadores + accesibilidad', () => {
  function mockIndicadores() {
    mswServer.use(
      http.get(ALERTAS, () =>
        HttpResponse.json({ items: [], offset: 0, limit: 1, total: 3 }),
      ),
      http.get(ANTIGUEDAD, ({ request }) => {
        const url = new URL(request.url);
        const moneda = url.searchParams.get('moneda');
        return HttpResponse.json({
          titulo: 'Antigüedad de saldos',
          generadoEn: '2026-07-14T12:00:00Z',
          filtrosAplicados: [],
          columnas: [],
          filas: [],
          totales: { vencido: moneda === 'MXN' ? 120000 : 500 },
        });
      }),
    );
  }

  it('muestra alertas pendientes y vencido POR moneda (sin cross-divisa)', async () => {
    mockIndicadores();
    render(<CxcLandingPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByTestId('indicadores-cxc')).toBeInTheDocument();
    expect(await screen.findByText('3')).toBeInTheDocument();
    expect(await screen.findByText(/120,000\.00 MXN/)).toBeInTheDocument();
    expect(await screen.findByText(/500\.00 USD/)).toBeInTheDocument();
  });

  it('sin cartera.leer no renderiza indicadores', async () => {
    useAuthStore.setState({
      permisos: ['cuentas_por_cobrar.lineas-credito.leer'],
    });
    render(<CxcLandingPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Cuentas por Cobrar')).toBeInTheDocument();
    expect(screen.queryByTestId('indicadores-cxc')).not.toBeInTheDocument();
  });

  it('cero issues axe-core (WCAG 2.1 AA) en el landing con indicadores', async () => {
    mockIndicadores();
    const { container } = render(<CxcLandingPage />, {
      wrapper: createQueryWrapper(),
    });
    await screen.findByTestId('indicadores-cxc');

    const results: AxeResults = await axe.run(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa'] },
    });

    if (results.violations.length > 0) {
      const detalle = results.violations
        .map((v) => `${v.id}: ${v.help} (${v.nodes.length} nodos)`)
        .join('\n');
      throw new Error(`Violaciones axe:\n${detalle}`);
    }
    expect(results.violations).toHaveLength(0);
  });
});
