import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { CanalesVentaPage } from '@/modules/administracion/components/CanalesVentaPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke del catálogo administrable de Canales de venta (FAC-ING-PR3).
 * Lista + columna clave A+W + gating del CRUD por permiso (el mismo de
 * sucursales) + inline form de alta.
 */
const CANALES = [
  {
    id: 1,
    nombre: 'Tienda Cancún',
    estatus: 0,
    version: 1,
    claveAw: 'CANCUN',
  },
  {
    id: 9,
    nombre: 'Planta Pintura',
    estatus: 1,
    version: 2,
    claveAw: null,
  },
];

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
  mswServer.use(
    http.get('*/api/v1/admin/canales-venta', () => HttpResponse.json(CANALES)),
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

describe('<CanalesVentaPage> — smoke (FAC-ING-PR3)', () => {
  it('renderiza la lista con clave A+W y badges de estatus', async () => {
    setPermisos([PermisosCanonicos.AdminEmpresasSucursalesGestionar]);
    render(<CanalesVentaPage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Tienda Cancún')).toBeInTheDocument();
    expect(screen.getByText('Planta Pintura')).toBeInTheDocument();
    // Columna clave A+W: GRUPPE de la ingesta de pedidos.
    expect(screen.getByText('CANCUN')).toBeInTheDocument();
    expect(screen.getByText('Activo')).toBeInTheDocument();
    expect(screen.getByText('Inactivo')).toBeInTheDocument();
  });

  it('con permiso: "Agregar canal" monta el inline form; el inactivo ofrece Reactivar', async () => {
    setPermisos([PermisosCanonicos.AdminEmpresasSucursalesGestionar]);
    render(<CanalesVentaPage />, { wrapper: createQueryWrapper() });

    await screen.findByText('Tienda Cancún');
    fireEvent.click(screen.getByRole('button', { name: /agregar canal/i }));
    expect(
      screen.getByRole('form', { name: /agregar canal/i }),
    ).toBeInTheDocument();

    expect(
      screen.getByRole('button', { name: /reactivar canal planta pintura/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /desactivar canal tienda cancún/i }),
    ).toBeInTheDocument();
  });

  it('sin permiso de gestión: lista visible pero sin acciones de CRUD', async () => {
    setPermisos([]);
    render(<CanalesVentaPage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Tienda Cancún')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /agregar canal/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /editar canal/i }),
    ).not.toBeInTheDocument();
  });

  it('estado error con retry cuando el backend falla', async () => {
    setPermisos([PermisosCanonicos.AdminEmpresasSucursalesGestionar]);
    mswServer.use(
      http.get('*/api/v1/admin/canales-venta', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Error interno', status: 500 },
          {
            status: 500,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );
    render(<CanalesVentaPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/No se pudieron cargar los canales de venta/i),
      ).toBeInTheDocument(),
    );
  });
});
