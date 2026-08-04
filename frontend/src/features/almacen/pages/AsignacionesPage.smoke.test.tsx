import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { AsignacionesPage } from '@/features/almacen/pages/AsignacionesPage';
import { useAuthStore } from '@/lib/auth/auth-store';

// Router mockeado con estado mutable: cada test fija el `search` y espía el
// `navigate` (el sentinel `desde` + `articuloEtiqueta` llegan por la URL).
const routerMock = vi.hoisted(() => ({
  search: {} as Record<string, unknown>,
  navigate: vi.fn(),
}));

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => routerMock.navigate,
  useSearch: () => routerMock.search,
}));

const UBIC = {
  id: 'ub-1',
  subAlmacenId: 'sa-1',
  clave: 'ÚNICA',
  nombre: 'Ubicación única',
  estatus: 0,
  esDefault: true,
  subAlmacenClave: 'INS-A',
  subAlmacenNombre: 'Insumos A',
  almacenClave: 'ALM-A',
  almacenNombre: 'Almacén A',
};

const ASIGNACION = {
  id: 'asig-1',
  ubicacionId: 'ub-1',
  articuloId: 'a-1',
  estatus: 0,
  articuloClave: 'ART-1',
  articuloDescripcion: 'Silicón',
};

beforeEach(() => {
  routerMock.search = {};
  routerMock.navigate.mockClear();
  mswServer.use(
    http.get('*/api/v1/almacen/ubicaciones', () =>
      HttpResponse.json({ items: [UBIC], offset: 0, limit: 500, total: 1 }),
    ),
    http.get('*/api/v1/catalogos/articulos', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
    ),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['almacen.asignaciones.leer', 'almacen.asignaciones.administrar'],
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

describe('<AsignacionesPage> — smoke', () => {
  it('estado empty muestra "Sin asignaciones"', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/asignaciones', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 }),
      ),
    );
    render(<AsignacionesPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin asignaciones/i)).toBeInTheDocument(),
    );
  });

  it('resuelve la ubicación con la ruta del padre + muestra el artículo', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/asignaciones', () =>
        HttpResponse.json({
          items: [ASIGNACION],
          offset: 0,
          limit: 500,
          total: 1,
        }),
      ),
    );
    render(<AsignacionesPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('ART-1 · Silicón')).toBeInTheDocument(),
    );
    // La ubicación se muestra con su ruta de padre (distingue las "ÚNICA").
    expect(screen.getByText('ALM-A › INS-A · ÚNICA')).toBeInTheDocument();
  });

  it('"Asignar artículo" abre el sheet', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/asignaciones', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 }),
      ),
    );
    render(<AsignacionesPage />, { wrapper: createQueryWrapper() });
    fireEvent.click(
      await screen.findByRole('button', { name: /Asignar artículo/i }),
    );
    await waitFor(() =>
      expect(
        screen.getByText(/Elige el artículo y la ubicación donde vivirá/i),
      ).toBeInTheDocument(),
    );
  });

  it('quitar pide confirmación y llama al endpoint desasignar', async () => {
    let desasignarLlamado = false;
    mswServer.use(
      http.get('*/api/v1/almacen/asignaciones', () =>
        HttpResponse.json({
          items: [ASIGNACION],
          offset: 0,
          limit: 500,
          total: 1,
        }),
      ),
      http.post('*/api/v1/almacen/asignaciones/:id/desasignar', () => {
        desasignarLlamado = true;
        return HttpResponse.json({ ...ASIGNACION, estatus: 1 });
      }),
    );
    render(<AsignacionesPage />, { wrapper: createQueryWrapper() });

    fireEvent.click(
      await screen.findByRole('button', {
        name: /Quitar ART-1 · Silicón de la ubicación/i,
      }),
    );
    const confirmar = await screen.findByRole('button', { name: /^Quitar$/i });
    fireEvent.click(confirmar);
    await waitFor(() => expect(desasignarLlamado).toBe(true));
  });

  // ── Atajo con retorno (sentinel `desde=articulo`) ──────────────────────

  function mockAsignacionesVacias() {
    mswServer.use(
      http.get('*/api/v1/almacen/asignaciones', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 }),
      ),
    );
  }

  it('desde=articulo + articuloEtiqueta → breadcrumb "Volver a {etiqueta}"', async () => {
    routerMock.search = {
      articuloId: 'a-1',
      desde: 'articulo',
      articuloEtiqueta: 'ART-1 · Silicón',
    };
    mockAsignacionesVacias();
    render(<AsignacionesPage />, { wrapper: createQueryWrapper() });

    expect(
      await screen.findByRole('button', { name: /Volver a ART-1 · Silicón/i }),
    ).toBeInTheDocument();
  });

  it('desde=articulo SIN articuloEtiqueta → breadcrumb con fallback genérico', async () => {
    routerMock.search = { articuloId: 'a-1', desde: 'articulo' };
    mockAsignacionesVacias();
    render(<AsignacionesPage />, { wrapper: createQueryWrapper() });

    expect(
      await screen.findByRole('button', { name: /Volver al artículo/i }),
    ).toBeInTheDocument();
  });

  it('sin desde (filtrado manual por articuloId) → NO hay breadcrumb', async () => {
    routerMock.search = { articuloId: 'a-1' };
    mockAsignacionesVacias();
    render(<AsignacionesPage />, { wrapper: createQueryWrapper() });

    await screen.findByText('Ubicación de artículos');
    expect(
      screen.queryByRole('button', { name: /Volver a/i }),
    ).not.toBeInTheDocument();
  });

  it('click en "Volver" navega al detalle del artículo con URL limpia', async () => {
    routerMock.search = {
      articuloId: 'a-1',
      desde: 'articulo',
      articuloEtiqueta: 'ART-1 · Silicón',
    };
    mockAsignacionesVacias();
    render(<AsignacionesPage />, { wrapper: createQueryWrapper() });

    fireEvent.click(
      await screen.findByRole('button', { name: /Volver a ART-1 · Silicón/i }),
    );

    expect(routerMock.navigate).toHaveBeenCalledWith({
      to: '/admin/datos-maestros/articulos/$id',
      params: { id: 'a-1' },
    });
  });
});
