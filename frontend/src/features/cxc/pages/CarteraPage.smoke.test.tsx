import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  AlineacionColumnaCxc,
  TipoColumnaReporteCxc,
} from '@/features/cxc/api/types';
import { useAuthStore } from '@/lib/auth/auth-store';

const searchMock = vi.hoisted(() => ({ current: {} as Record<string, unknown> }));

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => searchMock.current,
}));

import { CarteraPage } from '@/features/cxc/pages/CarteraPage';
import { AnticiposCxcPage } from '@/features/cxc/pages/AnticiposCxcPage';
import { EstadoCuentaPage } from '@/features/cxc/pages/EstadoCuentaPage';

const BASE = '*/api/v1/cuentas-por-cobrar';

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
      'cuentas_por_cobrar.lineas-credito.leer',
    ],
    errorMessage: null,
  });
  mswServer.use(
    http.get(`${BASE}/clientes-lookup`, () => HttpResponse.json([])),
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

describe('<CarteraPage> — smoke', () => {
  it('renderiza el reporte con buckets dinámicos del backend', async () => {
    mswServer.use(
      http.get(`${BASE}/cartera/antiguedad`, () =>
        HttpResponse.json({
          titulo: 'Antigüedad de saldos',
          generadoEn: '2026-07-14T12:00:00Z',
          filtrosAplicados: [{ label: 'Fecha de corte', valor: '2026-07-14' }],
          columnas: [
            {
              key: 'cliente',
              label: 'Cliente',
              tipo: TipoColumnaReporteCxc.Texto,
              alineacion: AlineacionColumnaCxc.Izquierda,
            },
            {
              key: 'bucket_16_30',
              label: '16-30 días',
              tipo: TipoColumnaReporteCxc.Moneda,
              alineacion: AlineacionColumnaCxc.Derecha,
            },
          ],
          filas: [{ cliente: 'ACME SA', bucket_16_30: 1234.5 }],
          totales: { bucket_16_30: 1234.5 },
        }),
      ),
    );
    render(<CarteraPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('16-30 días')).toBeInTheDocument();
    expect(screen.getByText('ACME SA')).toBeInTheDocument();
  });

  it('estado error', async () => {
    mswServer.use(
      http.get(`${BASE}/cartera/antiguedad`, () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Boom', status: 500 },
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<CarteraPage />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText(/No se pudo cargar el reporte/i),
    ).toBeInTheDocument();
  });
});

describe('<EstadoCuentaPage> — smoke', () => {
  it('sin cliente: CTA de selección', async () => {
    render(<EstadoCuentaPage />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText(/Elige un cliente para generar su estado/i),
    ).toBeInTheDocument();
  });

  it('con cliente: renderiza el reporte', async () => {
    searchMock.current = { clienteId: 'cli-1' };
    mswServer.use(
      http.get(`${BASE}/cartera/estado-cuenta/cli-1`, () =>
        HttpResponse.json({
          titulo: 'Estado de cuenta',
          generadoEn: '2026-07-14T12:00:00Z',
          filtrosAplicados: [],
          columnas: [
            {
              key: 'movimiento',
              label: 'Movimiento',
              tipo: TipoColumnaReporteCxc.Texto,
              alineacion: AlineacionColumnaCxc.Izquierda,
            },
          ],
          filas: [{ movimiento: 'Factura F-100' }],
          totales: null,
        }),
      ),
    );
    render(<EstadoCuentaPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Factura F-100')).toBeInTheDocument();
  });
});

describe('<AnticiposCxcPage> — smoke', () => {
  it('sin cliente: CTA de selección', async () => {
    render(<AnticiposCxcPage />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText(/Elige un cliente para ver sus anticipos/i),
    ).toBeInTheDocument();
  });

  it('con cliente: tabla + totales por moneda (nunca cross-divisa)', async () => {
    searchMock.current = { clienteId: 'cli-1' };
    mswServer.use(
      http.get(`${BASE}/anticipos`, () =>
        HttpResponse.json([
          {
            anticipoId: 'ant-1',
            clienteId: 'cli-1',
            estado: 'Abierto',
            montoCobrado: 100000,
            montoAmortizado: 40000,
            saldo: 60000,
            saldoDisponible: 60000,
            moneda: 'MXN',
            pedidoOrigenRef: '3000123456',
          },
          {
            anticipoId: 'ant-2',
            clienteId: 'cli-1',
            estado: 'Abierto',
            montoCobrado: 5000,
            montoAmortizado: 0,
            saldo: 5000,
            saldoDisponible: 5000,
            moneda: 'USD',
            pedidoOrigenRef: null,
          },
        ]),
      ),
    );
    render(<AnticiposCxcPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('3000123456')).toBeInTheDocument();
    // Totales separados por divisa.
    expect(screen.getByText(/Total MXN:/i)).toBeInTheDocument();
    expect(screen.getByText(/Total USD:/i)).toBeInTheDocument();
  });

  it('con cliente sin anticipos: empty', async () => {
    searchMock.current = { clienteId: 'cli-1' };
    mswServer.use(
      http.get(`${BASE}/anticipos`, () => HttpResponse.json([])),
    );
    render(<AnticiposCxcPage />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText(/El cliente no tiene anticipos/i),
    ).toBeInTheDocument();
  });
});
