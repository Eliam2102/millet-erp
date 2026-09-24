import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { EmpleadosPage } from '@/modules/administracion/components/EmpleadosPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

vi.mock('@tanstack/react-router', async (importOriginal) => {
  const original = await importOriginal<typeof import('@tanstack/react-router')>();
  return {
    ...original,
    Link: ({ children, params, className }: { children: React.ReactNode; params: { id: string }; className?: string }) =>
      <a href={`/admin/empleados/${params.id}`} className={className}>{children}</a>,
  };
});

/**
 * Smoke del master de Empleados (ADM-FE-PR1). Lista con email y clave
 * de puesto resuelta + gating del CRUD por permiso + inline form de
 * alta (con selectores de puesto/jefe/sucursal/departamento/usuario).
 */
const PUESTOS = {
  items: [
    {
      id: '00000000-0000-0000-0000-000000000002',
      clave: 'GER',
      nombre: 'Gerente',
      estatus: 0,
    },
  ],
  offset: 0,
  limit: 200,
  total: 1,
};

const EMPLEADOS = {
  items: [
    {
      id: 'aaaaaaaa-0000-0000-0000-000000000001',
      empresaId: 'e-1',
      clave: 'EMP-001',
      nombre: 'Juana Pérez',
      email: 'juana.perez@millet.mx',
      puestoId: '00000000-0000-0000-0000-000000000002',
      jefeDirectoId: null,
      sucursalId: null,
      departamentoId: null,
      usuarioId: null,
      estatus: 0,
    },
    {
      id: 'aaaaaaaa-0000-0000-0000-000000000002',
      empresaId: 'e-1',
      clave: 'EMP-002',
      nombre: 'Pedro López',
      email: null,
      puestoId: null,
      jefeDirectoId: null,
      sucursalId: null,
      departamentoId: null,
      usuarioId: null,
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
    http.get('*/api/v1/catalogos/empleados', () =>
      HttpResponse.json(EMPLEADOS),
    ),
    http.get('*/api/v1/catalogos/puestos', () => HttpResponse.json(PUESTOS)),
    http.get('*/api/v1/catalogos/sucursales', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
    http.get('*/api/v1/catalogos/departamentos', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
    http.get('*/api/v1/identidad/usuarios', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
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

describe('<EmpleadosPage> — smoke (ADM-FE-PR1)', () => {
  it('renderiza la lista con email, clave de puesto resuelta y badges', async () => {
    setPermisos([PermisosCanonicos.AdminEmpleadosGestionar]);
    render(<EmpleadosPage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Juana Pérez')).toBeInTheDocument();
    expect(screen.getByText('juana.perez@millet.mx')).toBeInTheDocument();
    expect(screen.getByText('Pedro López')).toBeInTheDocument();
    // Columna Puesto: id resuelto a clave vía GET /catalogos/puestos.
    expect(await screen.findByText('GER')).toBeInTheDocument();
    expect(screen.getByText('Activo')).toBeInTheDocument();
    expect(screen.getByText('Inactivo')).toBeInTheDocument();
  });

  it('con permiso: "Agregar empleado" monta el inline form; el inactivo ofrece Reactivar', async () => {
    setPermisos([PermisosCanonicos.AdminEmpleadosGestionar]);
    render(<EmpleadosPage />, { wrapper: createQueryWrapper() });

    await screen.findByText('Juana Pérez');
    fireEvent.click(screen.getByRole('button', { name: /agregar empleado/i }));
    expect(
      screen.getByRole('form', { name: /agregar empleado/i }),
    ).toBeInTheDocument();
    expect(screen.getByText(/Paso 1 de 5:/i)).toBeInTheDocument();
    expect(screen.getByText('Persona y sucursal')).toBeInTheDocument();

    expect(
      screen.getByRole('button', { name: /reactivar empleado EMP-002/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /desactivar empleado EMP-001/i }),
    ).toBeInTheDocument();
  });

  it('sin permiso de gestión: lista visible pero sin acciones de CRUD', async () => {
    setPermisos([]);
    render(<EmpleadosPage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Juana Pérez')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /agregar empleado/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /editar empleado/i }),
    ).not.toBeInTheDocument();
  });

  it('estado error con retry cuando el backend falla', async () => {
    setPermisos([PermisosCanonicos.AdminEmpleadosGestionar]);
    mswServer.use(
      http.get('*/api/v1/catalogos/empleados', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Error interno', status: 500 },
          {
            status: 500,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );
    render(<EmpleadosPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/No se pudieron cargar los empleados/i),
      ).toBeInTheDocument(),
    );
  });
});
