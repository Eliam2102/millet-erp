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
    {
      id: 'aaaaaaaa-0000-0000-0000-000000000003',
      empresaId: 'e-1',
      clave: 'EMP-003',
      nombre: 'Carlos Activo',
      email: 'carlos@millet.mx',
      puestoId: null,
      jefeDirectoId: null,
      sucursalId: null,
      departamentoId: null,
      usuarioId: 'u-3',
      usuarioActivo: true,
      estadoAcceso: 0,
      primerAccesoEn: '2026-09-10T10:00:00Z',
      estatus: 0,
    },
    {
      id: 'aaaaaaaa-0000-0000-0000-000000000004',
      empresaId: 'e-1',
      clave: 'EMP-004',
      nombre: 'Ana Pendiente Login',
      email: 'ana@millet.mx',
      emailContacto: 'ana.personal@gmail.com',
      puestoId: null,
      jefeDirectoId: null,
      sucursalId: null,
      departamentoId: null,
      usuarioId: 'u-4',
      usuarioActivo: true,
      estadoAcceso: 1,
      primerAccesoEn: null,
      accesoEnviadoEn: '2026-09-20T10:00:00Z',
      estatus: 0,
    },
  ],
  offset: 0,
  limit: 200,
  total: 4,
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
    expect(screen.getAllByText('Activo').length).toBeGreaterThan(0);
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
    expect(screen.getByText(/Paso 1 de 4:/i)).toBeInTheDocument();
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

  it('filtra por "Sin iniciar sesión" y por "Sin acceso"', async () => {
    setPermisos([PermisosCanonicos.AdminEmpleadosGestionar]);
    render(<EmpleadosPage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Juana Pérez')).toBeInTheDocument();
    expect(screen.getByText('Ana Pendiente Login')).toBeInTheDocument();
    expect(screen.getByText('Carlos Activo')).toBeInTheDocument();

    // Filtra por Sin iniciar sesión
    fireEvent.click(screen.getByRole('button', { name: /sin iniciar sesión/i }));
    expect(screen.getByText('Ana Pendiente Login')).toBeInTheDocument();
    expect(screen.queryByText('Carlos Activo')).not.toBeInTheDocument();
    expect(screen.queryByText('Juana Pérez')).not.toBeInTheDocument();

    // Filtra por Sin acceso
    fireEvent.click(screen.getByRole('button', { name: /sin acceso \(2\)/i }));
    expect(screen.getByText('Juana Pérez')).toBeInTheDocument();
    expect(screen.queryByText('Ana Pendiente Login')).not.toBeInTheDocument();
  });

  it('permite reenviar acceso al colaborador pendiente de primer login', async () => {
    let reenvioLlamado = false;
    mswServer.use(
      http.post(
        '*/api/v1/admin/colaboradores/:id/acceso/reenviar',
        async ({ params }) => {
          if (params.id === 'aaaaaaaa-0000-0000-0000-000000000004') {
            reenvioLlamado = true;
            return HttpResponse.json({
              empleadoId: params.id,
              usuarioId: 'u-4',
              email: 'ana@millet.mx',
              estadoAcceso: 1,
              motivoErrorProvision: null,
              accesoEnviadoEn: new Date().toISOString(),
              primerAccesoEn: null,
              emailContacto: 'ana.personal@gmail.com',
              usuarioActivo: true,
            });
          }
          return new HttpResponse(null, { status: 404 });
        },
      ),
    );

    setPermisos([
      PermisosCanonicos.AdminEmpleadosGestionar,
      PermisosCanonicos.IdentidadUsuariosCrear,
    ]);
    render(<EmpleadosPage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Ana Pendiente Login')).toBeInTheDocument();
    const btnReenviar = screen.getByRole('button', { name: /reenviar acceso/i });
    expect(btnReenviar).toBeInTheDocument();

    fireEvent.click(btnReenviar);

    await waitFor(() => {
      expect(reenvioLlamado).toBe(true);
    });
  });
});
