import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { MiCaja } from '@/features/facturacion/pages/MiCaja';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

const BASE = '*/api/v1/facturacion/cajas';

const sesionAbierta = {
  id: 'ses-1',
  cajaId: 'c-1',
  cajaNombre: 'Caja Mostrador Conkal',
  sucursalId: 's-1',
  responsableUsuarioId: 'u-test',
  estado: 'Abierta',
  diaOperacion: '2026-07-11',
  fechaApertura: '2026-07-11T14:00:00Z',
  fechaCierre: null,
  fondoApertura: 500,
  efectivoTeorico: null,
  efectivoDeclarado: null,
  diferencia: null,
  cierreExtemporaneo: false,
  notasCierre: null,
  autorizacionAperturaId: null,
  version: 1,
  cortes: [],
  movimientos: [
    {
      id: 'm-1',
      tipo: 'FondoApertura',
      formaPago: '01',
      importe: 500,
      moneda: 'MXN',
      descripcion: 'Fondo de apertura',
      referencia: null,
      cobroMostradorId: null,
      usuarioId: 'u-test',
      createdAt: '2026-07-11T14:00:00Z',
    },
  ],
  totalesPorForma: [{ formaPago: '01', montoSistema: 500, montoDeclarado: null }],
};

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['facturacion.caja.operar'],
    errorMessage: null,
  });
  mswServer.use(
    http.get('*/api/v1/facturacion/cobros', () => HttpResponse.json([])),
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

describe('<MiCaja> — smoke', () => {
  it('sin sesión: muestra el form de apertura', async () => {
    mswServer.use(
      http.get(`${BASE}/sesion-actual`, () =>
        HttpResponse.json({ sesion: null, diaAnteriorPendiente: false }),
      ),
      http.get(BASE, () =>
        HttpResponse.json([
          {
            id: 'c-1',
            nombre: 'Caja Mostrador Conkal',
            descripcion: null,
            estatus: 'Activo',
            sucursales: 1,
            canales: 1,
            usuarios: 1,
          },
        ]),
      ),
    );
    render(<MiCaja />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Abrir sesión/i, { selector: 'h2' })).toBeInTheDocument(),
    );
    expect(screen.getByLabelText(/Fondo de apertura/i)).toBeInTheDocument();
  });

  it('con sesión abierta: totales, movimientos y acciones', async () => {
    mswServer.use(
      http.get(`${BASE}/sesion-actual`, () =>
        HttpResponse.json({ sesion: sesionAbierta, diaAnteriorPendiente: false }),
      ),
    );
    render(<MiCaja />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Caja Mostrador Conkal')).toBeInTheDocument(),
    );
    expect(screen.getByText('Abierta')).toBeInTheDocument();
    expect(screen.getByText(/Fondo de apertura/)).toBeInTheDocument(); // movimiento
    expect(screen.getByRole('button', { name: /Iniciar arqueo/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Liquidar ruta/i })).toBeInTheDocument(); // PR7
  });

  it('sesión de día anterior: banner de bloqueo §5.2', async () => {
    mswServer.use(
      http.get(`${BASE}/sesion-actual`, () =>
        HttpResponse.json({ sesion: sesionAbierta, diaAnteriorPendiente: true }),
      ),
    );
    render(<MiCaja />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/día anterior/i)).toBeInTheDocument(),
    );
    expect(screen.queryByRole('button', { name: /Registrar movimiento/i })).toBeNull();
  });
});
