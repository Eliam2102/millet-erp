import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
// `render` is used inside renderConProvider below; keep import.
import { http, HttpResponse } from 'msw';
import type { ReactElement } from 'react';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { OrdenesCompraLayout } from '@/features/compras/ordenes/pages/OrdenesCompraLayout';
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

// Stub del provider para que <OrdenesCompraLayout> no truene al
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
    params,
    className,
    'aria-current': ariaCurrent,
  }: {
    children: React.ReactNode;
    to: string;
    params?: Record<string, string>;
    className?: string;
    'aria-current'?: string;
  }) => (
    <a
      href={params != null ? to.replace('$id', params.id ?? '') : to}
      className={className}
      aria-current={ariaCurrent}
    >
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
    http.get('*/api/v1/catalogos/sucursales', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
    http.get('*/api/v1/catalogos/almacenes', () =>
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
    folio: 'OC-2026-000001',
    folioAnio: 2026,
    estado: EstadoOrdenCompra.Borrador,
    subEstadoRecepcion: SubEstadoRecepcion.SinRecepcion,
    subEstadoFacturacion: SubEstadoFacturacion.SinFactura,
    subEstadoPago: SubEstadoPago.SinPago,
    proveedorId: 'p-1',
    compradorTitularId: 'u-1',
    moneda: 'MXN',
    fechaDocumento: '2026-05-09',
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

describe('<OrdenesCompraLayout> — smoke (UF1-PR2)', () => {
  it('sin idActivo + sin OCs: muestra empty state de la lista y placeholder del panel', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', () =>
        HttpResponse.json({ items: [], page: 1, pageSize: 50, totalCount: 0 }),
      ),
    );
    renderConProvider(<OrdenesCompraLayout idActivo={null} />);
    expect(
      await screen.findByText(/aún no hay órdenes de compra/i),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/selecciona una orden de compra/i),
    ).toBeInTheDocument();
  });

  it('con idActivo + detalle: renderiza el detalle en el panel', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', () =>
        HttpResponse.json({ items: [], page: 1, pageSize: 50, totalCount: 0 }),
      ),
    );
    renderConProvider(
      <OrdenesCompraLayout
        idActivo="oc-1"
        detalle={<div>DETALLE-OC-MOCK</div>}
      />,
    );
    expect(screen.getByText('DETALLE-OC-MOCK')).toBeInTheDocument();
  });

  it('lista con items: renderiza folio + EstadoBadge OC y refleja idActivo', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', () =>
        HttpResponse.json({
          items: [makeOrdenResumen({ id: 'oc-99', folio: 'OC-2026-000099' })],
          page: 1,
          pageSize: 50,
          totalCount: 1,
        }),
      ),
    );
    renderConProvider(
      <OrdenesCompraLayout idActivo="oc-99" detalle={<div>D</div>} />,
    );
    await waitFor(() =>
      expect(screen.getByText('OC-2026-000099')).toBeInTheDocument(),
    );
    const link = screen.getByText('OC-2026-000099').closest('a');
    expect(link).toHaveAttribute('aria-current', 'page');
  });

  it('renderiza filtros compactos: dropdown Estado + Vistas rápidas + Más filtros', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', () =>
        HttpResponse.json({ items: [], page: 1, pageSize: 50, totalCount: 0 }),
      ),
    );
    renderConProvider(<OrdenesCompraLayout idActivo={null} />);
    // El nuevo aside compacto reemplaza los 7 chips inline por un
    // dropdown "Vistas rápidas" + popover "Más filtros".
    expect(
      await screen.findByRole('button', { name: /Vistas rápidas/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Más filtros/i }),
    ).toBeInTheDocument();
  });

  it('con permiso ComprasOrdenesCrear muestra botón "Nueva" inline', async () => {
    useAuthStore.setState((s) => ({
      ...s,
      permisos: ['compras.ordenes.leer', 'compras.ordenes.crear'],
    }));
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', () =>
        HttpResponse.json({ items: [], page: 1, pageSize: 50, totalCount: 0 }),
      ),
    );
    renderConProvider(<OrdenesCompraLayout idActivo={null} />);
    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: /Nueva/i }),
      ).toBeInTheDocument(),
    );
  });
});
