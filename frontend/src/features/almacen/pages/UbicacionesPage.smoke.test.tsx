import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { UbicacionesPage } from '@/features/almacen/pages/UbicacionesPage';
import { useAuthStore } from '@/lib/auth/auth-store';

const searchMock = vi.hoisted(() => ({ value: {} as Record<string, unknown> }));

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => () => {},
  useSearch: () => searchMock.value,
}));

const UBIC_DEFAULT = {
  id: 'ub-def',
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

const UBIC_REAL = {
  id: 'ub-real',
  subAlmacenId: 'sa-1',
  clave: 'HG1-84',
  nombre: 'Rack HG1 fila 84',
  estatus: 0,
  esDefault: false,
  subAlmacenClave: 'INS-A',
  subAlmacenNombre: 'Insumos A',
  almacenClave: 'ALM-A',
  almacenNombre: 'Almacén A',
};

function mockList(items: unknown[]) {
  mswServer.use(
    http.get('*/api/v1/almacen/ubicaciones', () =>
      HttpResponse.json({ items, offset: 0, limit: 500, total: items.length }),
    ),
  );
}

beforeEach(() => {
  // El SubAlmacenSelector del filtro consulta sub-almacenes + almacenes.
  mswServer.use(
    http.get('*/api/v1/almacen/sub-almacenes', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 }),
    ),
    http.get('*/api/v1/almacen/almacenes', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 }),
    ),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['almacen.ubicaciones.leer', 'almacen.ubicaciones.administrar'],
    errorMessage: null,
  });
});

afterEach(() => {
  searchMock.value = {};
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

describe('<UbicacionesPage> — smoke', () => {
  it('estado empty muestra "Sin ubicaciones"', async () => {
    mockList([]);
    render(<UbicacionesPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin ubicaciones/i)).toBeInTheDocument(),
    );
  });

  it('crear: el sheet hereda el sub-almacén del filtro y postea con Idempotency-Key', async () => {
    // El sub-almacén viene del filtro activo (SubAlmacenSelector); el sheet lo
    // pre-selecciona y el test solo captura clave/nombre.
    const SA_ID = '11111111-1111-1111-1111-111111111111';
    searchMock.value = { subAlmacenId: SA_ID };
    let bodyRecibido: unknown = null;
    let idempotencyKey: string | null = null;
    mockList([]);
    mswServer.use(
      http.post('*/api/v1/almacen/ubicaciones', async ({ request }) => {
        bodyRecibido = await request.json();
        idempotencyKey = request.headers.get('Idempotency-Key');
        return HttpResponse.json(
          { id: 'ub-new', subAlmacenId: SA_ID, clave: 'HG1-84' },
          { status: 201 },
        );
      }),
    );
    render(<UbicacionesPage />, { wrapper: createQueryWrapper() });

    fireEvent.click(
      await screen.findByRole('button', { name: /Nueva ubicación/i }),
    );
    fireEvent.change(await screen.findByPlaceholderText('Ej. HG1-84'), {
      target: { value: 'HG1-84' },
    });
    fireEvent.change(
      screen.getByPlaceholderText(/Nombre descriptivo/i),
      { target: { value: 'Rack HG1 fila 84' } },
    );
    fireEvent.click(screen.getByRole('button', { name: /Crear ubicación/i }));

    await waitFor(() => expect(bodyRecibido).not.toBeNull());
    expect(bodyRecibido).toMatchObject({
      subAlmacenId: SA_ID,
      clave: 'HG1-84',
      nombre: 'Rack HG1 fila 84',
      estatus: 0,
    });
    // Idempotency-Key fresco por submit (fix PR B): un UUID en el header.
    expect(idempotencyKey).toMatch(/^[0-9a-f-]{36}$/i);
  });

  it('la ÚNICA (default) va protegida: badge + sin botón desactivar, pero editable', async () => {
    mockList([UBIC_DEFAULT]);
    render(<UbicacionesPage />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('default de enrutamiento')).toBeInTheDocument(),
    );
    // Se puede editar clave/nombre…
    expect(
      screen.getByRole('button', { name: /Editar ubicación ÚNICA/i }),
    ).toBeInTheDocument();
    // …pero NO desactivar (blindada; destino del trigger de saldos).
    expect(
      screen.queryByRole('button', { name: /Desactivar ubicación ÚNICA/i }),
    ).not.toBeInTheDocument();
  });

  it('una ubicación real activa se puede desactivar (confirma + llama al endpoint)', async () => {
    let desactivarLlamado = false;
    mockList([UBIC_REAL]);
    mswServer.use(
      http.post('*/api/v1/almacen/ubicaciones/:id/desactivar', () => {
        desactivarLlamado = true;
        return HttpResponse.json({ id: 'ub-real', estatus: 1 });
      }),
    );
    render(<UbicacionesPage />, { wrapper: createQueryWrapper() });

    fireEvent.click(
      await screen.findByRole('button', {
        name: /Desactivar ubicación HG1-84/i,
      }),
    );
    const confirmar = await screen.findByRole('button', {
      name: /^Desactivar$/i,
    });
    fireEvent.click(confirmar);
    await waitFor(() => expect(desactivarLlamado).toBe(true));
  });

  it('baja bloqueada por saldo: el backend responde EN_USO y la fila permanece', async () => {
    let desactivarLlamado = false;
    mockList([UBIC_REAL]);
    mswServer.use(
      http.post('*/api/v1/almacen/ubicaciones/:id/desactivar', () => {
        desactivarLlamado = true;
        return HttpResponse.json(
          {
            type: 'about:blank',
            title: 'No se puede desactivar',
            status: 422,
            code: 'UBICACION_EN_USO_CON_SALDO',
          },
          { status: 422 },
        );
      }),
    );
    render(<UbicacionesPage />, { wrapper: createQueryWrapper() });

    fireEvent.click(
      await screen.findByRole('button', {
        name: /Desactivar ubicación HG1-84/i,
      }),
    );
    fireEvent.click(
      await screen.findByRole('button', { name: /^Desactivar$/i }),
    );
    await waitFor(() => expect(desactivarLlamado).toBe(true));
    // El guardrail bloquea: la fila sigue activa (no se removió/inactivó).
    expect(
      await screen.findByRole('button', {
        name: /Desactivar ubicación HG1-84/i,
      }),
    ).toBeInTheDocument();
  });
});
