import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ReordenPage } from '@/features/almacen/pages/ReordenPage';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

// Fila Almacén (N2): objetivo Máximo (=1) resalta la columna Máx.
const FILA_ALMACEN = {
  id: 'cfg-1',
  articuloId: 'a-1',
  nivel: 1,
  entidadId: 'alm-1',
  minimo: 10,
  maximo: 100,
  puntoReorden: 30,
  autoRequisicion: true,
  objetivo: 1,
  estatus: 0,
  articuloClave: 'ART-1',
  articuloDescripcion: 'Silicón',
};
// Fila Sucursal (N1): objetivo Punto de reorden (=2).
const FILA_SUCURSAL = {
  id: 'cfg-2',
  articuloId: 'a-2',
  nivel: 0,
  entidadId: 'suc-1',
  minimo: 5,
  maximo: 50,
  puntoReorden: 15,
  autoRequisicion: false,
  objetivo: 2,
  estatus: 0,
  articuloClave: 'ART-2',
  articuloDescripcion: 'Interlayer',
};

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/catalogos/sucursales', () =>
      HttpResponse.json({
        items: [{ id: 'suc-1', clave: 'MTY', nombre: 'Monterrey' }],
        offset: 0,
        limit: 200,
        total: 1,
      }),
    ),
    http.get('*/api/v1/catalogos/almacenes', () =>
      HttpResponse.json({
        items: [
          { id: 'alm-1', clave: 'ALM-A', nombre: 'Almacén A', sucursalId: 'suc-1' },
        ],
        offset: 0,
        limit: 200,
        total: 1,
      }),
    ),
    http.get('*/api/v1/catalogos/articulos', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
    ),
    // Interruptor del motor (banner): default apagado, como el seed real.
    http.get('*/api/v1/almacen/configuracion', () =>
      HttpResponse.json({ empresaId: 'e-test', reabastoAutomaticoActivo: false }),
    ),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['almacen.reorden.leer', 'almacen.reorden.administrar'],
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

describe('<ReordenPage> — smoke', () => {
  it('estado empty muestra "Sin configuraciones de reabasto"', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/reorden', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 }),
      ),
    );
    render(<ReordenPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/Sin configuraciones de reabasto/i),
      ).toBeInTheDocument(),
    );
  });

  it('resuelve el nombre de la entidad polimórficamente y marca el objetivo activo', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/reorden', () =>
        HttpResponse.json({
          items: [FILA_ALMACEN, FILA_SUCURSAL],
          offset: 0,
          limit: 500,
          total: 2,
        }),
      ),
    );
    render(<ReordenPage />, { wrapper: createQueryWrapper() });

    // Artículo (clave · descripción).
    await waitFor(() =>
      expect(screen.getByText('ART-1 · Silicón')).toBeInTheDocument(),
    );
    // Entidad resuelta: almacén (N2) desde el catálogo de almacenes…
    expect(screen.getByText('Almacén A')).toBeInTheDocument();
    // …y sucursal (N1) desde el catálogo de sucursales.
    expect(screen.getByText('Monterrey')).toBeInTheDocument();
    // Exactamente una celda-objetivo resaltada por fila (Máx en una, P.reorden en otra).
    expect(screen.getAllByTitle('Objetivo de reabasto')).toHaveLength(2);
  });

  it('"Nuevo reabasto" abre el sheet en modo crear', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/reorden', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 }),
      ),
    );
    render(<ReordenPage />, { wrapper: createQueryWrapper() });
    fireEvent.click(
      await screen.findByRole('button', { name: /Nuevo reabasto/i }),
    );
    await waitFor(() =>
      expect(
        screen.getByText(/Define el punto de reabasto de un artículo/i),
      ).toBeInTheDocument(),
    );
  });

  it('el lápiz abre el sheet en edición con la llave bloqueada', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/reorden', () =>
        HttpResponse.json({
          items: [FILA_ALMACEN],
          offset: 0,
          limit: 500,
          total: 1,
        }),
      ),
    );
    render(<ReordenPage />, { wrapper: createQueryWrapper() });
    fireEvent.click(
      await screen.findByRole('button', { name: /Editar reabasto ART-1/i }),
    );
    await waitFor(() =>
      expect(
        screen.getByText(/La llave .* no se puede cambiar/i),
      ).toBeInTheDocument(),
    );
  });

  it('desactivar pide confirmación y llama al endpoint', async () => {
    let desactivarLlamado = false;
    mswServer.use(
      http.get('*/api/v1/almacen/reorden', () =>
        HttpResponse.json({
          items: [FILA_ALMACEN],
          offset: 0,
          limit: 500,
          total: 1,
        }),
      ),
      http.post('*/api/v1/almacen/reorden/:id/desactivar', () => {
        desactivarLlamado = true;
        return HttpResponse.json({ ...FILA_ALMACEN, estatus: 1 });
      }),
    );
    render(<ReordenPage />, { wrapper: createQueryWrapper() });

    fireEvent.click(
      await screen.findByRole('button', { name: /Desactivar reabasto ART-1/i }),
    );
    // Diálogo de confirmación.
    const confirmar = await screen.findByRole('button', {
      name: /^Desactivar$/i,
    });
    fireEvent.click(confirmar);
    await waitFor(() => expect(desactivarLlamado).toBe(true));
  });

  it('banner del motor: muestra el estado y el confirm dispara el PATCH', async () => {
    let patchLlamado = false;
    mswServer.use(
      http.get('*/api/v1/almacen/reorden', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 }),
      ),
      http.patch('*/api/v1/almacen/configuracion', () => {
        patchLlamado = true;
        return HttpResponse.json({
          empresaId: 'e-test',
          reabastoAutomaticoActivo: true,
        });
      }),
    );
    render(<ReordenPage />, { wrapper: createQueryWrapper() });

    // Estado visible (default apagado).
    expect(
      await screen.findByText(/Reabasto automático:/),
    ).toBeInTheDocument();
    expect(screen.getByText(/Apagado/)).toBeInTheDocument();

    // Encender → diálogo con los avisos de peso → confirmar dispara el PATCH.
    fireEvent.click(screen.getByRole('button', { name: /^Encender$/ }));
    expect(
      await screen.findByText(/puede tardar hasta 1 hora/i),
    ).toBeInTheDocument();
    fireEvent.click(
      within(screen.getByRole('alertdialog')).getByRole('button', {
        name: /^Encender$/,
      }),
    );
    await waitFor(() => expect(patchLlamado).toBe(true));
    // El estado del banner refleja la respuesta del PATCH.
    expect(await screen.findByText(/Encendido/)).toBeInTheDocument();
  });

  it('banner del motor: sin almacen.reorden.administrar no hay botón de cambio', async () => {
    useAuthStore.setState({ permisos: ['almacen.reorden.leer'] });
    mswServer.use(
      http.get('*/api/v1/almacen/reorden', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 }),
      ),
    );
    render(<ReordenPage />, { wrapper: createQueryWrapper() });

    expect(
      await screen.findByText(/Reabasto automático:/),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /^Encender$|^Apagar$/ }),
    ).not.toBeInTheDocument();
  });
});
