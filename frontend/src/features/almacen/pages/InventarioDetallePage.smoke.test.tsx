import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { InventarioDetallePage } from '@/features/almacen/pages/InventarioDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EstadoConteo } from '@/features/almacen/api/types';

// Controla qué rutas hijas "matchean" en cada test. El detalle debe ceder
// el render al <Outlet/> cuando captura/aprobación están activas —
// regresión del bug donde las pantallas dedicadas eran inaccesibles
// (el detalle no renderizaba Outlet y siempre se pintaba a sí mismo).
const matchRouteMock = vi.fn<(args: { to: string }) => boolean>(() => false);

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    className,
  }: {
    children: React.ReactNode;
    to: string;
    className?: string;
  }) => (
    <a href={to} className={className}>
      {children}
    </a>
  ),
  Outlet: () => <div data-testid="outlet-ruta-hija" />,
  useParams: () => ({ id: 'c-1' }),
  useNavigate: () => vi.fn(),
  useMatchRoute: () => matchRouteMock,
}));

const CONTEO_DETALLE = {
  id: 'c-1',
  tipo: 0,
  estado: EstadoConteo.EnCurso,
  fechaPlanificada: '2026-07-09',
  subAlmacenId: 's-1',
  filtroFamilia: null,
  responsableId: 'u-1',
  fechaInicio: '2026-07-09T08:00:00Z',
  snapshotCapturadoAt: '2026-07-09T08:00:00Z',
  aprobadorId: null,
  fechaAprobacion: null,
  version: 1,
  cantidadLineas: 2,
  cantidadCapturadas: 0,
  subAlmacenClave: 'HG1',
  responsableNombre: 'Almacenista Uno',
  aprobadorNombre: null,
};

beforeEach(() => {
  matchRouteMock.mockReset();
  matchRouteMock.mockReturnValue(false);
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: [
      'almacen.inventarios.leer',
      'almacen.inventarios.capturar',
      'almacen.inventarios.crear',
    ],
    errorMessage: null,
  });
  mswServer.use(
    http.get('*/api/v1/almacen/conteos/c-1', () =>
      HttpResponse.json(CONTEO_DETALLE),
    ),
  );
});

afterEach(() => {
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    user: null,
    permisos: [],
  });
});

describe('InventarioDetallePage', () => {
  it('renderiza el detalle del conteo cuando ninguna ruta hija está activa', async () => {
    render(<InventarioDetallePage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText(/Conteo Rotativo/)).toBeInTheDocument();
    expect(screen.queryByTestId('outlet-ruta-hija')).not.toBeInTheDocument();

    // Etiquetas legibles en la cabecera — nunca GUID: sub-almacén por
    // clave y responsable por nombre.
    expect(screen.getByText('HG1')).toBeInTheDocument();
    expect(screen.getByText('Almacenista Uno')).toBeInTheDocument();
    expect(screen.queryByText('s-1')).not.toBeInTheDocument();
    expect(screen.queryByText('u-1')).not.toBeInTheDocument();
  });

  it('cede el render al Outlet cuando la ruta de captura está activa (regresión: pantallas hijas inaccesibles)', () => {
    matchRouteMock.mockImplementation(
      ({ to }) => to === '/almacen/inventarios/$id/captura',
    );

    render(<InventarioDetallePage />, { wrapper: createQueryWrapper() });

    expect(screen.getByTestId('outlet-ruta-hija')).toBeInTheDocument();
    expect(screen.queryByText(/Conteo Rotativo/)).not.toBeInTheDocument();
  });

  it('cede el render al Outlet cuando la ruta de aprobación está activa', () => {
    matchRouteMock.mockImplementation(
      ({ to }) => to === '/almacen/inventarios/$id/aprobacion',
    );

    render(<InventarioDetallePage />, { wrapper: createQueryWrapper() });

    expect(screen.getByTestId('outlet-ruta-hija')).toBeInTheDocument();
    expect(screen.queryByText(/Conteo Rotativo/)).not.toBeInTheDocument();
  });
});
