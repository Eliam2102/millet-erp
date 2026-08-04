import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ArticuloDetalle } from '@/modules/datos-maestros/components/ArticuloDetalle';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke del detalle de artículo (P3 del patrón cross-módulo). Análogo
 * a <c>ProveedorDetalle.smoke.test</c>: mockea router y usa MSW.
 */
const routerMock = vi.hoisted(() => ({ navigate: vi.fn() }));

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    className,
    ...rest
  }: {
    children: React.ReactNode;
    to: string;
    className?: string;
    [key: string]: unknown;
  }) => (
    <a href={to} className={className} {...rest}>
      {children}
    </a>
  ),
  useParams: () => ({ id: 'a-1' }),
  useNavigate: () => routerMock.navigate,
}));

const ARTICULO_DETALLE = {
  id: 'a-1',
  clave: 'A-001',
  claveLegacy: null,
  nombre: 'Caja de cartón 30x30x30',
  descripcionLarga: 'Caja kraft doble corrugado',
  unidadMedidaDefault: 'PZA',
  unidadMedidaId: null,
  naturaleza: 0,
  categoria: 'Empaque',
  categoriaId: null,
  precioReferenciaMonto: 12.5,
  precioReferenciaMoneda: 'MXN',
  estatus: 0,
};

function setPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos,
    errorMessage: null,
  });
}

beforeEach(() => {
  routerMock.navigate.mockClear();
  // Base: gestiona artículos pero SIN permiso de ubicaciones → el botón "Ver
  // ubicaciones" no aparece (los tests que lo prueban añaden el permiso).
  setPermisos([
    PermisosCanonicos.DatosMaestrosArticulosGestionar,
    PermisosCanonicos.CompartidoCatalogosAdministrar,
  ]);
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

describe('<ArticuloDetalle> — smoke', () => {
  it('renderiza header con clave + badge Activo y form con nombre', async () => {
    mswServer.use(
      http.get('*/api/v1/datos-maestros/articulos/a-1', () =>
        HttpResponse.json(ARTICULO_DETALLE),
      ),
    );

    render(<ArticuloDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText('A-001')).toBeInTheDocument());

    expect(screen.getByText('Activo')).toBeInTheDocument();
    expect(
      screen.getByDisplayValue('Caja de cartón 30x30x30'),
    ).toBeInTheDocument();
    // Unidad y categoría son selectores del catálogo; un artículo legacy
    // (FK null) muestra su string legacy como contexto (ADR-0046).
    expect(screen.getByText(/Legacy: «PZA»/)).toBeInTheDocument();
    expect(screen.getByText(/Legacy: «Empaque»/)).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /^desactivar$/i }),
    ).toBeInTheDocument();
  });

  it('oculta el botón Desactivar cuando el artículo está inactivo', async () => {
    mswServer.use(
      http.get('*/api/v1/datos-maestros/articulos/a-1', () =>
        HttpResponse.json({ ...ARTICULO_DETALLE, estatus: 1 }),
      ),
    );

    render(<ArticuloDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText('A-001')).toBeInTheDocument());

    expect(
      screen.queryByRole('button', { name: /^desactivar$/i }),
    ).not.toBeInTheDocument();
    expect(screen.getByText('Inactivo')).toBeInTheDocument();
  });

  // ── Atajo "Ver ubicaciones (N)" (gateado por almacen.asignaciones.leer) ──

  function mockArticulo() {
    mswServer.use(
      http.get('*/api/v1/datos-maestros/articulos/a-1', () =>
        HttpResponse.json(ARTICULO_DETALLE),
      ),
    );
  }

  it('con permiso .leer: botón visible y badge muestra el total del conteo', async () => {
    setPermisos([
      PermisosCanonicos.CompartidoCatalogosAdministrar,
      PermisosCanonicos.AlmacenAsignacionesRead,
    ]);
    mockArticulo();
    mswServer.use(
      http.get('*/api/v1/almacen/asignaciones', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 1, total: 3 }),
      ),
    );

    render(<ArticuloDetalle />, { wrapper: createQueryWrapper() });

    expect(
      await screen.findByRole('button', { name: /Ver ubicaciones \(3\)/i }),
    ).toBeInTheDocument();
  });

  it('sin permiso .leer: botón NO visible y la query de conteo NO dispara', async () => {
    // beforeEach ya deja permisos SIN almacen.asignaciones.leer.
    let conteoLlamado = false;
    mockArticulo();
    mswServer.use(
      http.get('*/api/v1/almacen/asignaciones', () => {
        conteoLlamado = true;
        return HttpResponse.json({ items: [], offset: 0, limit: 1, total: 0 });
      }),
    );

    render(<ArticuloDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText('A-001')).toBeInTheDocument());
    expect(
      screen.queryByRole('button', { name: /Ver ubicaciones/i }),
    ).not.toBeInTheDocument();
    expect(conteoLlamado).toBe(false);
  });

  it('click navega con { articuloId, desde, articuloEtiqueta } correctos', async () => {
    setPermisos([PermisosCanonicos.AlmacenAsignacionesRead]);
    mockArticulo();
    mswServer.use(
      http.get('*/api/v1/almacen/asignaciones', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 1, total: 3 }),
      ),
    );

    render(<ArticuloDetalle />, { wrapper: createQueryWrapper() });

    fireEvent.click(
      await screen.findByRole('button', { name: /Ver ubicaciones/i }),
    );

    expect(routerMock.navigate).toHaveBeenCalledWith({
      to: '/almacen/asignaciones',
      search: {
        articuloId: 'a-1',
        desde: 'articulo',
        articuloEtiqueta: 'A-001 · Caja de cartón 30x30x30',
      },
    });
  });

  it('N=0: botón visible con "(0)"', async () => {
    setPermisos([PermisosCanonicos.AlmacenAsignacionesRead]);
    mockArticulo();
    mswServer.use(
      http.get('*/api/v1/almacen/asignaciones', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 1, total: 0 }),
      ),
    );

    render(<ArticuloDetalle />, { wrapper: createQueryWrapper() });

    expect(
      await screen.findByRole('button', { name: /Ver ubicaciones \(0\)/i }),
    ).toBeInTheDocument();
  });
});
