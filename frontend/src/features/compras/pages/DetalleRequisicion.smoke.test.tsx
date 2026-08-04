import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DetalleRequisicion } from '@/features/compras/pages/DetalleRequisicion';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  Clasificacion,
  EstadoRequisicion,
  Prioridad,
} from '@/features/compras/api/types';
import {
  NuevaOrdenCompraContext,
  type NuevaOrdenCompraApi,
} from '@/features/compras/ordenes/components/nueva-orden-compra-context';
import type { ReactNode } from 'react';

// AccionesRequisicion (renderizado dentro de DetalleRequisicion) inyecta
// useNuevaOrdenCompra() para el botón "Convertir a OC" (PR-B). Stub no-op
// para que el test no requiera el Provider real (vive en routes/_app.tsx).
const stubNuevaOcApi: NuevaOrdenCompraApi = {
  abrir: () => {},
  cerrar: () => {},
  setDirty: () => {},
};
function wrapperConStubs() {
  const QueryWrapper = createQueryWrapper();
  return function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryWrapper>
        <NuevaOrdenCompraContext.Provider value={stubNuevaOcApi}>
          {children}
        </NuevaOrdenCompraContext.Provider>
      </QueryWrapper>
    );
  };
}

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    className,
    search,
    ...rest
  }: {
    children: React.ReactNode;
    to: string;
    className?: string;
    search?: unknown;
    asChild?: boolean;
    [key: string]: unknown;
  }) => (
    <a
      href={to}
      className={className}
      data-search={JSON.stringify(search)}
      {...rest}
    >
      {children}
    </a>
  ),
  useParams: () => ({ id: 'rq-1' }),
  useLocation: () => ({
    pathname: '/compras/requisiciones/rq-1',
    state: undefined,
  }),
  // El componente lee matches para inferir si está en
  // /requisiciones/$id o /pendientes/$id (botón "Cerrar" de la
  // sub-topbar). En tests por default simulamos /requisiciones.
  useMatches: () => [
    { routeId: '/_app/compras/requisiciones/$id' as const },
  ],
}));

function setupCatalogos() {
  mswServer.use(
    http.get('*/api/v1/catalogos/departamentos', () =>
      HttpResponse.json({
        items: [{ id: 'd-1', nombre: 'Mantenimiento', clave: 'MTO' }],
        offset: 0,
        limit: 200,
        total: 1,
      }),
    ),
    http.get('*/api/v1/catalogos/sucursales', () =>
      HttpResponse.json({
        items: [{ id: 's-1', nombre: 'Toluca', clave: 'TOL' }],
        offset: 0,
        limit: 200,
        total: 1,
      }),
    ),
    http.get('*/api/v1/catalogos/almacenes', () =>
      HttpResponse.json({
        items: [{ id: 'a-1', nombre: 'Almacén central' }],
        offset: 0,
        limit: 200,
        total: 1,
      }),
    ),
    http.get('*/api/v1/identidad/usuarios', () =>
      HttpResponse.json({
        items: [{ id: 'u-1', nombre: 'Pedro García', email: 'p@m.com' }],
        offset: 0,
        limit: 200,
        total: 1,
      }),
    ),
    // Histórico (UF7-PR4): el detalle dispara el fetch al montar.
    // Default vacío; tests que necesiten entradas específicas lo
    // sobrescriben con su propio handler.
    http.get('*/api/v1/compras/requisiciones/*/historico', () =>
      HttpResponse.json([]),
    ),
  );
}

const RQ_BASE = {
  id: 'rq-1',
  empresaId: 'e-1',
  folio: 'MID2026-000042',
  folioAnio: 2026,
  clasificacion: Clasificacion.Servicio,
  sucursalId: 's-1',
  departamentoId: 'd-1',
  almacenDestinoId: 'a-1',
  requisitanteId: 'u-1',
  creadorId: 'u-1',
  descripcion: null,
  prioridad: Prioridad.Normal,
  fechaSolicitud: '2026-05-09T10:00:00Z',
  fechaEntregaDeseada: null,
  proveedorSugeridoId: null,
  estado: EstadoRequisicion.Borrador,
  motivoTerminacionId: null,
  motivoTerminacionTexto: null,
  actorTerminacionId: null,
  fechaTerminacion: null,
  version: 1,
  createdAt: '2026-05-09T10:00:00Z',
  updatedAt: '2026-05-09T10:00:00Z',
  lineas: [],
  autorizaciones: [],
};

beforeEach(() => {
  setupCatalogos();
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'p@m.com', nombre: 'Pedro García' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: ['compras.requisiciones.leer'],
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

describe('<DetalleRequisicion> — smoke', () => {
  it('renderiza folio, cabecera y secciones', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones/rq-1', () =>
        HttpResponse.json(RQ_BASE),
      ),
    );
    render(<DetalleRequisicion />, { wrapper: wrapperConStubs() });
    await waitFor(() =>
      expect(
        screen.getAllByText('MID2026-000042').length,
      ).toBeGreaterThan(0),
    );
    expect(screen.getByText('Líneas')).toBeInTheDocument();
    // UF7-PR4: el histórico vive en un modal; en el sub-topbar hay un
    // botón "Histórico" que lo abre.
    expect(
      screen.getByRole('button', { name: /^histórico/i }),
    ).toBeInTheDocument();
    // design/frontend-polish: en master-detail, el "volver" es un
    // botón de cerrar (X) que vuelve a /compras/requisiciones.
    expect(
      screen.getByRole('link', { name: /^cerrar$/i }),
    ).toBeInTheDocument();
    // Botón Imprimir — Ctrl+P + click en topbar son equivalentes.
    expect(
      screen.getByRole('button', { name: /imprimir/i }),
    ).toBeInTheDocument();
  });

  it('estado loading: muestra el breadcrumb con "Cargando…"', () => {
    mswServer.use(
      http.get(
        '*/api/v1/compras/requisiciones/rq-1',
        () => new Promise(() => {}),
      ),
    );
    render(<DetalleRequisicion />, { wrapper: wrapperConStubs() });
    expect(screen.getByText(/Cargando…/i)).toBeInTheDocument();
  });

  it('error 403: delega a DetalleErrorBoundary', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones/rq-1', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Sin permiso',
            status: 403,
            code: 'PERMISO_DENEGADO',
          },
          {
            status: 403,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );
    render(<DetalleRequisicion />, { wrapper: wrapperConStubs() });
    await waitFor(() =>
      expect(
        screen.getByText(/no tienes permiso/i),
      ).toBeInTheDocument(),
    );
  });

  it('estado EnAutorizacion: muestra el panel "Disponibilidad estimada" (PR-C)', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones/rq-1', () =>
        HttpResponse.json({
          ...RQ_BASE,
          estado: EstadoRequisicion.EnAutorizacion,
          lineas: [
            {
              id: 'l-1',
              posicion: 1,
              articuloId: 'art-1',
              cantidad: 10,
              unidadMedida: 'PZA',
              precioEstimadoMonto: 100,
              precioEstimadoMoneda: 'MXN',
              cuentaContableId: null,
              centroCostoId: null,
              proyecto: null,
              fechaRequerida: null,
              notas: null,
              cantDeAlmacen: 0,
              cantDeCompra: 0,
              cantRecibida: 0,
              cantPendiente: 10,
              reservaId: null,
            },
          ],
        }),
      ),
      // PR-C: el detalle auto-fetch el preview en EnAutorizacion.
      http.get(
        '*/api/v1/compras/requisiciones/rq-1/cubrimiento-estimado',
        () =>
          HttpResponse.json({
            requisicionId: 'rq-1',
            aplica: true,
            lineas: [
              {
                lineaId: 'l-1',
                articuloId: 'art-1',
                cantidad: 10,
                estimadoDeAlmacen: 4,
                estimadoDeCompra: 6,
                disponible: 4,
              },
            ],
          }),
      ),
    );
    render(<DetalleRequisicion />, { wrapper: wrapperConStubs() });
    await waitFor(() =>
      expect(screen.getByText('Disponibilidad estimada')).toBeInTheDocument(),
    );
    // El disclaimer deja claro que es estimación y no reserva.
    expect(screen.getByText(/no reserva stock/i)).toBeInTheDocument();
  });

  it('estado Autorizada con líneas: muestra ResumenCubrimiento', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones/rq-1', () =>
        HttpResponse.json({
          ...RQ_BASE,
          estado: EstadoRequisicion.Autorizada,
          lineas: [
            {
              id: 'l-1',
              posicion: 1,
              articuloId: 'art-1',
              cantidad: 10,
              unidadMedida: 'PZA',
              precioEstimadoMonto: 100,
              precioEstimadoMoneda: 'MXN',
              cuentaContableId: null,
              centroCostoId: null,
              proyecto: null,
              fechaRequerida: null,
              notas: null,
              cantDeAlmacen: 5,
              cantDeCompra: 5,
              cantRecibida: 0,
              cantPendiente: 0,
              reservaId: null,
            },
          ],
        }),
      ),
    );
    render(<DetalleRequisicion />, { wrapper: wrapperConStubs() });
    await waitFor(() =>
      expect(screen.getByText(/Cubrimiento global/i)).toBeInTheDocument(),
    );
  });
});
