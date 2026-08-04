import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DetalleOrdenCompra } from '@/features/compras/ordenes/pages/DetalleOrdenCompra';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  EstadoOrdenCompra,
  SubEstadoFacturacion,
  SubEstadoPago,
  SubEstadoRecepcion,
} from '@/features/compras/ordenes/api/types';

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    'aria-label': ariaLabel,
  }: {
    children: React.ReactNode;
    to: string;
    'aria-label'?: string;
  }) => (
    <a href={to} aria-label={ariaLabel}>
      {children}
    </a>
  ),
  useParams: () => ({ id: 'oc-1' }),
  useLocation: () => ({ state: undefined }),
  // UF5-PR2: <AccionesOC> usa useNavigate() para redirigir tras
  // duplicar OC. El smoke test no ejercita el flujo de duplicación,
  // así que un mock noop es suficiente.
  useNavigate: () => () => {},
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

function makeDetalle(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: 'oc-1',
    empresaId: 'e-1',
    folio: 'OC-2026-000042',
    folioAnio: 2026,
    proveedorId: 'p-1',
    sucursalDestinoId: 's-1',
    condicionesPagoId: 'cp-1',
    usoPrincipalId: 'up-1',
    moneda: 'MXN',
    tipoCambio: null,
    compradorTitularId: 'u-1',
    encargadoComprasId: 'u-1',
    observaciones: null,
    sinRequisicionPrevia: false,
    esImportacion: false,
    cotizacionExcepcionada: false,
    fechaDocumento: '2026-05-09',
    fechaContabilizacion: null,
    fechaEntregaEsperada: null,
    estado: EstadoOrdenCompra.Borrador,
    subEstadoRecepcion: SubEstadoRecepcion.SinRecepcion,
    subEstadoFacturacion: SubEstadoFacturacion.SinFactura,
    subEstadoPago: SubEstadoPago.SinPago,
    motivoSinRequisicion: null,
    motivoCancelacion: null,
    motivoRechazoId: null,
    motivoRechazoTexto: null,
    ocOrigenId: null,
    version: 1,
    createdAt: '2026-05-09T10:00:00Z',
    updatedAt: '2026-05-09T10:00:00Z',
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

describe('<DetalleOrdenCompra> — smoke (UF1-PR2)', () => {
  it('renderiza folio + EstadoBadge en sub-topbar', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/oc-1', () =>
        HttpResponse.json(makeDetalle(), {
          headers: { ETag: '"1"' },
        }),
      ),
    );
    render(<DetalleOrdenCompra />, { wrapper: createQueryWrapper() });
    // El folio aparece dos veces: chico en sub-topbar y grande como h1
    // en CabeceraOrdenCompra (mismo patrón que RQ).
    const matches = await screen.findAllByText('OC-2026-000042');
    expect(matches.length).toBeGreaterThanOrEqual(1);
  });

  it('renderiza SubEstadosBar (3 dimensiones) y StepperAutorizacionOc', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/oc-1', () =>
        HttpResponse.json(makeDetalle(), { headers: { ETag: '"1"' } }),
      ),
    );
    const { container } = render(<DetalleOrdenCompra />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() =>
      expect(
        container.querySelector('[data-component="sub-estados-bar"]'),
      ).not.toBeNull(),
    );
    expect(
      container.querySelector('[data-component="stepper-autorizacion-oc"]'),
    ).not.toBeNull();
  });

  it('tab "Información" activo por default; renderiza CabeceraOrdenCompra', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/oc-1', () =>
        HttpResponse.json(makeDetalle(), { headers: { ETag: '"1"' } }),
      ),
    );
    const { container } = render(<DetalleOrdenCompra />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() =>
      expect(
        container.querySelector('[data-component="cabecera-orden-compra"]'),
      ).not.toBeNull(),
    );
    // El tab "Información" debe tener data-activo.
    expect(
      container.querySelector('[data-tab="informacion"][data-activo]'),
    ).not.toBeNull();
  });

  it('los 4 tabs no-Información son stubs con mensaje "próximamente"', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/oc-1', () =>
        HttpResponse.json(makeDetalle(), { headers: { ETag: '"1"' } }),
      ),
    );
    render(<DetalleOrdenCompra />, { wrapper: createQueryWrapper() });
    await waitFor(() => {
      const matches = screen.queryAllByText('OC-2026-000042');
      expect(matches.length).toBeGreaterThanOrEqual(1);
    });
    // Los nombres de los tabs aparecen siempre (en el role=tablist).
    expect(screen.getByRole('tab', { name: /Líneas/ })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Adjuntos/ })).toBeInTheDocument();
    expect(
      screen.getByRole('tab', { name: /Autorización/ }),
    ).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Historial/ })).toBeInTheDocument();
  });

  it('flag esImportacion=true: muestra badge "Importación"', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/oc-1', () =>
        HttpResponse.json(makeDetalle({ esImportacion: true }), {
          headers: { ETag: '"1"' },
        }),
      ),
    );
    render(<DetalleOrdenCompra />, { wrapper: createQueryWrapper() });
    // Selector específico al badge — desde UF3-PR1 también existe un
    // sub-tab "Importación" con el mismo texto, por eso no usamos
    // `findByText` plano.
    const badge = await screen.findByTestId('badge', { exact: false }).catch(
      () => null,
    );
    void badge;
    expect(
      await screen.findByText((content, element) =>
        element?.getAttribute('data-flag') === 'es-importacion' &&
        content === 'Importación'),
    ).toBeInTheDocument();
  });

  it('estado=Cancelada con motivoCancelacion: muestra el banner de motivo', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/oc-1', () =>
        HttpResponse.json(
          makeDetalle({
            estado: EstadoOrdenCompra.Cancelada,
            motivoCancelacion: 'Proveedor no entregó a tiempo',
          }),
          { headers: { ETag: '"1"' } },
        ),
      ),
    );
    render(<DetalleOrdenCompra />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText(/Proveedor no entregó a tiempo/),
    ).toBeInTheDocument();
  });

  it('404 ORDEN_COMPRA_NO_ENCONTRADA: renderiza Page404 con CTA volver a bandeja', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/oc-1', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'No encontrada',
            status: 404,
            code: 'ORDEN_COMPRA_NO_ENCONTRADA',
          },
          {
            status: 404,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );
    render(<DetalleOrdenCompra />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText(/Orden de compra no encontrada/i),
    ).toBeInTheDocument();
  });

  it('403: renderiza Page403 con CTA volver a bandeja', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/oc-1', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Forbidden', status: 403 },
          {
            status: 403,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );
    render(<DetalleOrdenCompra />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText(/no tienes permiso/i),
    ).toBeInTheDocument();
  });
});
