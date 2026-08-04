import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReactElement } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaOrdenesCompra } from '@/features/compras/ordenes/pages/BandejaOrdenesCompra';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  EstadoOrdenCompra,
  SubEstadoFacturacion,
  SubEstadoPago,
  SubEstadoRecepcion,
} from '@/features/compras/ordenes/api/types';
import {
  NuevaOrdenCompraContext,
  type NuevaOrdenCompraApi,
} from '@/features/compras/ordenes/components/nueva-orden-compra-context';

// Stub del provider para que <BandejaOrdenesCompra> no truene al
// llamar useNuevaOrdenCompra(). El comportamiento del Sheet real se
// testea aparte (SheetNuevaOC.test).
const mockNuevaOC: NuevaOrdenCompraApi = {
  abrir: () => {},
  cerrar: () => {},
  setDirty: () => {},
};

function renderConProvider(ui: ReactElement) {
  return render(
    <NuevaOrdenCompraContext.Provider value={mockNuevaOC}>
      {ui}
    </NuevaOrdenCompraContext.Provider>,
    { wrapper: createQueryWrapper() },
  );
}

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    className,
    state,
  }: {
    children: React.ReactNode;
    to: string;
    className?: string;
    state?: unknown;
  }) => (
    <a href={to} className={className} data-state={JSON.stringify(state)}>
      {children}
    </a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({ page: 1, pageSize: 50 }),
}));

function setupCatalogos() {
  mswServer.use(
    http.get('*/api/v1/catalogos/proveedores', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
    http.get('*/api/v1/identidad/usuarios', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
  );
}

function makeOrdenResumen(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: 'oc-1',
    folio: 'OC-MID2026-000001',
    folioAnio: 2026,
    estado: EstadoOrdenCompra.Borrador,
    subEstadoRecepcion: SubEstadoRecepcion.SinRecepcion,
    subEstadoFacturacion: SubEstadoFacturacion.SinFactura,
    subEstadoPago: SubEstadoPago.SinPago,
    proveedorId: 'p-1',
    compradorTitularId: 'u-1',
    moneda: 'MXN',
    fechaDocumento: '2026-05-12',
    referenciaProveedor: null,
    ...overrides,
  };
}

beforeEach(() => {
  setupCatalogos();
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['compras.ordenes.leer'],
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

describe('<BandejaOrdenesCompra> — smoke (UF1-PR3)', () => {
  it('estado loading: muestra el header sin truenos', () => {
    mswServer.use(
      http.get(
        '*/api/v1/compras/ordenes',
        () =>
          new Promise(() => {
            /* never resolves */
          }),
      ),
    );
    renderConProvider(<BandejaOrdenesCompra />);
    expect(screen.getByText('Bandeja de OCs')).toBeInTheDocument();
  });

  it('estado empty: muestra EmptyState con mensaje OC-específico', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', () =>
        HttpResponse.json({ items: [], page: 1, pageSize: 50, totalCount: 0 }),
      ),
    );
    renderConProvider(<BandejaOrdenesCompra />);
    expect(
      await screen.findByText(/aún no hay órdenes de compra/i),
    ).toBeInTheDocument();
  });

  it('estado data: renderiza tabla con folio, fecha, estado, sub-estados, link "Ver"', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', () =>
        HttpResponse.json({
          items: [
            makeOrdenResumen({
              id: 'oc-99',
              folio: 'OC-MID2026-000099',
              estado: EstadoOrdenCompra.EnAutorizacionJefeCompras,
              subEstadoRecepcion: SubEstadoRecepcion.Parcial,
            }),
          ],
          page: 1,
          pageSize: 50,
          totalCount: 1,
        }),
      ),
    );
    renderConProvider(<BandejaOrdenesCompra />);
    await waitFor(() =>
      expect(screen.getByText('OC-MID2026-000099')).toBeInTheDocument(),
    );
    // Estado badge OC y dot R con progreso parcial visible.
    expect(
      screen.getByText('En autorización Jefe de Compras'),
    ).toBeInTheDocument();
    // <HoverSubEstados> (UF6-PR1): un solo tooltip combinado en el wrapper
    // multi-línea + chips internos con `data-progreso`. Buscamos el chip R
    // por su atributo `data-sub-estado="recepcion"`.
    const wrapper = screen.getByRole('img', {
      name: /Recepción: Recepción parcial[\s\S]*Facturación[\s\S]*Pago/i,
    });
    expect(wrapper).toBeInTheDocument();
    const chipR = wrapper.querySelector('[data-sub-estado="recepcion"]');
    expect(chipR).toHaveAttribute('data-progreso', 'parcial');
    // Link "Ver" presente
    expect(screen.getAllByRole('link', { name: /Ver/i }).length).toBeGreaterThan(0);
  });

  it('estado error: muestra ErrorState con retry', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Internal Server Error',
            status: 500,
            traceId: 'abc',
          },
          {
            status: 500,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );
    renderConProvider(<BandejaOrdenesCompra />);
    await waitFor(
      () => expect(screen.getByRole('button', { name: /reintentar/i })).toBeInTheDocument(),
      { timeout: 3000 },
    );
  });

  it('expone filtros compactos: dropdown Estado + Vistas rápidas + Más filtros', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', () =>
        HttpResponse.json({ items: [], page: 1, pageSize: 50, totalCount: 0 }),
      ),
    );
    renderConProvider(<BandejaOrdenesCompra />);
    expect(
      await screen.findByRole('button', { name: /Vistas rápidas/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Más filtros/i }),
    ).toBeInTheDocument();
  });

  it('paginación: muestra "Mostrando X-Y de Z" con valores correctos', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', () =>
        HttpResponse.json({
          items: [makeOrdenResumen()],
          page: 1,
          pageSize: 50,
          totalCount: 1,
        }),
      ),
    );
    renderConProvider(<BandejaOrdenesCompra />);
    await waitFor(() =>
      expect(screen.getByText(/Mostrando 1–1 de 1/i)).toBeInTheDocument(),
    );
  });

  it('los 3 dots R/F/P aparecen con data-sub-estado para tests/scraping', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', () =>
        HttpResponse.json({
          items: [
            makeOrdenResumen({
              subEstadoRecepcion: SubEstadoRecepcion.Completa,
              subEstadoFacturacion: SubEstadoFacturacion.Parcial,
              subEstadoPago: SubEstadoPago.SinPago,
            }),
          ],
          page: 1,
          pageSize: 50,
          totalCount: 1,
        }),
      ),
    );
    const { container } = renderConProvider(<BandejaOrdenesCompra />);
    await waitFor(() =>
      expect(screen.getByText('OC-MID2026-000001')).toBeInTheDocument(),
    );
    expect(
      container.querySelector('[data-sub-estado="recepcion"][data-progreso="completo"]'),
    ).not.toBeNull();
    expect(
      container.querySelector('[data-sub-estado="facturacion"][data-progreso="parcial"]'),
    ).not.toBeNull();
    expect(
      container.querySelector('[data-sub-estado="pago"][data-progreso="vacio"]'),
    ).not.toBeNull();
  });
});

