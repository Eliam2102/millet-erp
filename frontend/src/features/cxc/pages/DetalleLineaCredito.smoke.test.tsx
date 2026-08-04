import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DetalleLineaCredito } from '@/features/cxc/pages/DetalleLineaCredito';
import {
  EstadoLineaCredito,
  OrigenLineaCredito,
} from '@/features/cxc/api/types';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
  useParams: () => ({ id: 'lc-1' }),
}));

const LINEAS = '*/api/v1/cuentas-por-cobrar/lineas-credito';
const LOOKUP = '*/api/v1/cuentas-por-cobrar/clientes-lookup';
const DISPONIBLE = '*/api/v1/cuentas-por-cobrar/credito-disponible/cli-1';

function mockLinea(estado: EstadoLineaCredito, motivoBloqueo: string | null = null) {
  return {
    id: 'lc-1',
    clienteId: 'cli-1',
    moneda: 'MXN',
    limite: 500000,
    origen: OrigenLineaCredito.Solunion,
    plazoDias: 30,
    clasificacion: 'A',
    estado,
    motivoBloqueo,
    version: 1,
  };
}

function mockHandlers(estado: EstadoLineaCredito, motivo: string | null = null) {
  mswServer.use(
    http.get(`${LINEAS}/lc-1`, () => HttpResponse.json(mockLinea(estado, motivo))),
    http.get(LOOKUP, () =>
      HttpResponse.json([
        { id: 'cli-1', clave: 'C001', rfc: 'AAA010101AAA', razonSocial: 'ACME SA' },
      ]),
    ),
    http.get(DISPONIBLE, () =>
      HttpResponse.json({
        clienteId: 'cli-1',
        lineas: [
          {
            lineaCreditoId: 'lc-1',
            moneda: 'MXN',
            limite: 500000,
            facturado: 120000,
            liberadoSinFactura: 0,
            disponible: 380000,
            estado,
            origen: OrigenLineaCredito.Solunion,
            plazoDias: 30,
          },
        ],
        datoIncompleto: true,
      }),
    ),
  );
}

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: [
      'cuentas_por_cobrar.lineas-credito.leer',
      'cuentas_por_cobrar.lineas-credito.gestionar',
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

describe('<DetalleLineaCredito> — smoke', () => {
  it('línea Activa: datos + crédito disponible con badge datoIncompleto + acciones', async () => {
    mockHandlers(EstadoLineaCredito.Activa);
    render(<DetalleLineaCredito />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText('ACME SA')).toBeInTheDocument());
    // Fórmula del disponible visible y badge del gap G1.
    expect(await screen.findByTestId('badge-dato-incompleto')).toBeInTheDocument();
    expect(screen.getByText(/380,000\.00 MXN/)).toBeInTheDocument();
    // Acciones de línea activa.
    expect(screen.getByRole('button', { name: /Bloquear/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Editar/i })).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Desbloquear/i }),
    ).not.toBeInTheDocument();
  });

  it('línea Bloqueada: banner con motivo + botón Desbloquear', async () => {
    mockHandlers(EstadoLineaCredito.Bloqueada, 'Cartera vencida +90 días');
    render(<DetalleLineaCredito />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('Cartera vencida +90 días')).toBeInTheDocument(),
    );
    expect(
      screen.getByRole('button', { name: /Desbloquear/i }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /^Bloquear/i }),
    ).not.toBeInTheDocument();
  });

  it('sin permiso gestionar: sin acciones de mutación', async () => {
    useAuthStore.setState({
      permisos: ['cuentas_por_cobrar.lineas-credito.leer'],
    });
    mockHandlers(EstadoLineaCredito.Activa);
    render(<DetalleLineaCredito />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText('ACME SA')).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: /Bloquear/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Editar/i })).not.toBeInTheDocument();
  });

  it('error del detalle con retry', async () => {
    mswServer.use(
      http.get(`${LINEAS}/lc-1`, () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Boom', status: 500 },
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<DetalleLineaCredito />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/No se pudo cargar la línea/i),
      ).toBeInTheDocument(),
    );
  });
});
