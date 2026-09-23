import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { PuestosPage } from '@/modules/administracion/components/PuestosPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke del catálogo de Puestos (ADM-FE-PR1). Lista + badges de
 * estatus + gating del CRUD por permiso + inline form de alta.
 */
const PUESTOS = {
  items: [
    {
      id: '00000000-0000-0000-0000-000000000001',
      clave: 'EJEC',
      nombre: 'Ejecutivo',
      estatus: 0,
    },
    {
      id: '00000000-0000-0000-0000-000000000003',
      clave: 'OPER',
      nombre: 'Operativo',
      estatus: 1,
    },
  ],
  offset: 0,
  limit: 200,
  total: 2,
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
  mswServer.use(
    http.get('*/api/v1/admin/puestos', () => HttpResponse.json(PUESTOS)),
    http.get('*/api/v1/catalogos/puestos', () => HttpResponse.json(PUESTOS)),
    http.get('*/api/v1/identidad/roles', () =>
      HttpResponse.json({ items: [], total: 0 }),
    ),
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

describe('<PuestosPage> — smoke (ADM-FE-PR1)', () => {
  it('renderiza la lista con clave y badges de estatus', async () => {
    setPermisos([PermisosCanonicos.AdminPuestosGestionar]);
    render(<PuestosPage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Ejecutivo')).toBeInTheDocument();
    expect(screen.getByText('Operativo')).toBeInTheDocument();
    expect(screen.getByText('EJEC')).toBeInTheDocument();
    expect(screen.getByText('Activo')).toBeInTheDocument();
    expect(screen.getByText('Inactivo')).toBeInTheDocument();
  });

  it('con permiso: "Agregar puesto" monta el inline form; el inactivo ofrece Reactivar', async () => {
    setPermisos([PermisosCanonicos.AdminPuestosGestionar]);
    render(<PuestosPage />, { wrapper: createQueryWrapper() });

    await screen.findByText('Ejecutivo');
    fireEvent.click(screen.getByRole('button', { name: /agregar puesto/i }));
    expect(
      screen.getByRole('form', { name: /agregar puesto/i }),
    ).toBeInTheDocument();

    expect(
      screen.getByRole('button', { name: /reactivar puesto OPER/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /desactivar puesto EJEC/i }),
    ).toBeInTheDocument();
  });

  it('sin permiso de gestión: lista visible pero sin acciones de CRUD', async () => {
    setPermisos([]);
    render(<PuestosPage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Ejecutivo')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /agregar puesto/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /editar puesto/i }),
    ).not.toBeInTheDocument();
  });

  it('estado error con retry cuando el backend falla', async () => {
    setPermisos([PermisosCanonicos.AdminPuestosGestionar]);
    mswServer.use(
      http.get('*/api/v1/admin/puestos', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Error interno', status: 500 },
          {
            status: 500,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );
    render(<PuestosPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/No se pudieron cargar los puestos/i),
      ).toBeInTheDocument(),
    );
  });
});
