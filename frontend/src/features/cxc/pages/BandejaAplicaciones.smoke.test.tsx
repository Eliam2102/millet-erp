import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { EstadoPropuestaAplicacion } from '@/features/cxc/api/types';
import { useAuthStore } from '@/lib/auth/auth-store';

const searchMock = vi.hoisted(() => ({ current: {} as Record<string, unknown> }));

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => searchMock.current,
  useParams: () => ({ id: 'pap-1' }),
}));

const abrirSpy = vi.fn();
vi.mock('@/features/cxc/components/nueva-propuesta-context', () => ({
  useNuevaPropuesta: () => ({
    abrir: abrirSpy,
    cerrar: () => {},
    setDirty: () => {},
  }),
}));

import { BandejaAplicaciones } from '@/features/cxc/pages/BandejaAplicaciones';
import { DetalleAplicacion } from '@/features/cxc/pages/DetalleAplicacion';

const BASE = '*/api/v1/cuentas-por-cobrar/propuestas-aplicacion';
const LOOKUP = '*/api/v1/cuentas-por-cobrar/clientes-lookup';

const propuesta = {
  id: 'pap-1',
  clienteId: 'cli-1',
  depositoRef: 'SPEI #123',
  montoDeposito: 25000,
  moneda: 'MXN',
  remittanceRef: 'REM-1',
  ajusteNoFiscal: -120,
  estado: EstadoPropuestaAplicacion.Propuesta,
  motivoRechazo: null,
  resueltaPor: null,
  resueltaEn: null,
  facturas: [
    {
      facturaCarteraId: 'fc-1',
      facturaUuid: 'uuid-0001',
      folio: 'F-100',
      importeAplicado: 25120,
      numParcialidad: 2,
    },
  ],
  version: 1,
};

beforeEach(() => {
  abrirSpy.mockClear();
  searchMock.current = {};
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: [
      'cuentas_por_cobrar.aplicacion-pago.proponer',
      'cuentas_por_cobrar.aplicacion-pago.confirmar',
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

describe('<BandejaAplicaciones> — smoke', () => {
  it('empty + botón Nueva propuesta con permiso proponer', async () => {
    mswServer.use(
      http.get(BASE, () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<BandejaAplicaciones />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText(/Sin propuestas de aplicación/i),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Nueva propuesta/i }),
    ).toBeInTheDocument();
  });

  it('renderiza propuesta con estado, ajuste y cliente resuelto', async () => {
    mswServer.use(
      http.get(BASE, () =>
        HttpResponse.json({
          items: [propuesta],
          offset: 0,
          limit: 200,
          total: 1,
        }),
      ),
    );
    render(<BandejaAplicaciones />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('SPEI #123')).toBeInTheDocument();
    expect(await screen.findByText('ACME SA')).toBeInTheDocument();
    expect(screen.getByText('Propuesta')).toBeInTheDocument();
  });

  it('estado error', async () => {
    mswServer.use(
      http.get(BASE, () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Boom', status: 500 },
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<BandejaAplicaciones />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText(/No se pudieron cargar las propuestas/i),
    ).toBeInTheDocument();
  });
});

describe('<DetalleAplicacion> — smoke', () => {
  it('pendiente: matching + acciones de Ingresos', async () => {
    mswServer.use(
      http.get(`${BASE}/pap-1`, () => HttpResponse.json(propuesta)),
    );
    render(<DetalleAplicacion />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('SPEI #123')).toBeInTheDocument();
    // Folio en vez del UUID fiscal (feedback 2026-07-14).
    expect(screen.getByText(/F-100/)).toBeInTheDocument();
    expect(screen.queryByText(/uuid-0001/)).not.toBeInTheDocument();
    expect(screen.getByText(/parc\. 2/)).toBeInTheDocument();
    expect(screen.getAllByText(/ajuste no fiscal/i).length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: /Confirmar/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Rechazar/i })).toBeInTheDocument();
  });

  it('rechazada: banner con motivo y sin acciones', async () => {
    mswServer.use(
      http.get(`${BASE}/pap-1`, () =>
        HttpResponse.json({
          ...propuesta,
          estado: EstadoPropuestaAplicacion.Rechazada,
          motivoRechazo: 'Cliente equivocado',
        }),
      ),
    );
    render(<DetalleAplicacion />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Cliente equivocado')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Confirmar/i }),
    ).not.toBeInTheDocument();
  });

  it('sin permiso confirmar: sin acciones de Ingresos', async () => {
    useAuthStore.setState({
      permisos: [
        'cuentas_por_cobrar.aplicacion-pago.proponer',
        'cuentas_por_cobrar.lineas-credito.leer',
      ],
    });
    mswServer.use(
      http.get(`${BASE}/pap-1`, () => HttpResponse.json(propuesta)),
    );
    render(<DetalleAplicacion />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('SPEI #123')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Confirmar/i }),
    ).not.toBeInTheDocument();
  });
});
