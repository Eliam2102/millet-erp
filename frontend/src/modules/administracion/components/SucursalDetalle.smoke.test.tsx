import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, fireEvent, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SucursalDetalle } from '@/modules/administracion/components/SucursalDetalle';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke del detalle de sucursal (F1-ADM-01 Fase 3 frontend). Mismo
 * approach que <c>EmpresaDetalle.smoke.test.tsx</c>: mockea
 * <c>@tanstack/react-router</c> (Link → anchor, useParams → id fijo) y
 * MSW para los endpoints reales. Cubre:
 *
 * <list>
 *   <item>Render de las 3 tabs (Departamentos | Puestos | Usuarios).</item>
 *   <item>Bloqueo 403 (<c>SUCURSAL_NO_ASOCIADA</c>) en una tab.</item>
 *   <item>Flujo básico de asociar un departamento existente.</item>
 * </list>
 */
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
  useParams: () => ({ id: 's-1' }),
}));

const SUCURSALES_CATALOGO = {
  items: [
    { id: 's-1', clave: 'S01', nombre: 'Sucursal Uno', estatus: 0 },
    { id: 's-2', clave: 'S02', nombre: 'Sucursal Dos', estatus: 0 },
  ],
  total: 2,
};

const DEPARTAMENTOS_CATALOGO = {
  items: [
    { id: 'd-1', clave: 'D01', nombre: 'Compras', estatus: 0 },
    { id: 'd-2', clave: 'D02', nombre: 'Ventas', estatus: 0 },
  ],
  total: 2,
};

const PUESTOS_CATALOGO = {
  items: [{ id: 'p-1', clave: 'P01', nombre: 'Gerente', estatus: 0 }],
  total: 1,
};

const USUARIOS_CATALOGO = {
  items: [
    {
      id: 'u-1',
      email: 'ana@millet.mx',
      entraOid: 'oid-1',
      nombre: 'Ana Pérez',
      departamentoId: null,
      activo: true,
      version: 1,
    },
  ],
  total: 1,
};

function setAuth(permisos: string[]) {
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
  setAuth([
    PermisosCanonicos.CompartidoCatalogosLeer,
    PermisosCanonicos.AdminSucursalesDepartamentosGestionar,
    PermisosCanonicos.AdminSucursalesPuestosGestionar,
    PermisosCanonicos.AdminSucursalesUsuariosGestionar,
  ]);

  mswServer.use(
    http.get('*/api/v1/catalogos/sucursales', () =>
      HttpResponse.json(SUCURSALES_CATALOGO),
    ),
    http.get('*/api/v1/catalogos/departamentos', () =>
      HttpResponse.json(DEPARTAMENTOS_CATALOGO),
    ),
    http.get('*/api/v1/catalogos/puestos', () =>
      HttpResponse.json(PUESTOS_CATALOGO),
    ),
    http.get('*/api/v1/identidad/usuarios/admin', () =>
      HttpResponse.json(USUARIOS_CATALOGO),
    ),
    http.get('*/api/v1/admin/empresas/sucursales/s-1/puestos', () =>
      HttpResponse.json({ items: [], total: 0 }),
    ),
    http.get('*/api/v1/admin/empresas/sucursales/s-1/usuarios', () =>
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

describe('<SucursalDetalle> — smoke', () => {
  it('renderiza las tabs Departamentos | Puestos | Usuarios y el header con la clave', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/empresas/sucursales/s-1/departamentos', () =>
        HttpResponse.json({ items: [], total: 0 }),
      ),
    );

    render(<SucursalDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText('S01')).toBeInTheDocument());

    const tabs = screen.getAllByRole('tab');
    expect(tabs).toHaveLength(2);
    expect(
      screen.getByRole('tab', { name: /^departamentos y puestos$/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('tab', { name: /^empleados$/i }),
    ).toBeInTheDocument();

    // Tab activa por default: Departamentos y Puestos. Al no haber asignados todavía,
    // muestra el empty state sin listar el catálogo completo.
    await waitFor(() =>
      expect(screen.getByText(/no hay departamentos asignados/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole('button', { name: /asignar primer departamento/i })).toBeInTheDocument();
  });

  it('muestra el estado de bloqueo cuando el backend responde 403 en una tab', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/empresas/sucursales/s-1/departamentos', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'No tienes acceso a esta sucursal.',
            status: 403,
            code: 'SUCURSAL_NO_ASOCIADA',
            traceId: 'trace-403',
          },
          { status: 403 },
        ),
      ),
    );

    render(<SucursalDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText('S01')).toBeInTheDocument());

    await waitFor(() =>
      expect(
        screen.getByText(/no tienes acceso a esta sucursal/i),
      ).toBeInTheDocument(),
    );
    // No debe caer al ErrorState genérico ("Reintentar" solo aparece
    // ahí, el estado 403 no ofrece retry).
    expect(
      screen.queryByRole('button', { name: /reintentar/i }),
    ).not.toBeInTheDocument();
  });

  it('permite asociar un departamento existente a la sucursal', async () => {
    let asignado = false;

    mswServer.use(
      http.get('*/api/v1/admin/empresas/sucursales/s-1/departamentos', () =>
        HttpResponse.json(
          asignado
            ? {
                items: [
                  {
                    sucursalId: 's-1',
                    departamentoId: 'd-1',
                    departamentoClave: 'D01',
                    departamentoNombre: 'Compras',
                    estatus: 0,
                    version: 1,
                  },
                ],
                total: 1,
              }
            : { items: [], total: 0 },
        ),
      ),
      http.post(
        '*/api/v1/admin/empresas/sucursales/s-1/departamentos/d-1',
        () => {
          asignado = true;
          return HttpResponse.json(
            {
              sucursalId: 's-1',
              departamentoId: 'd-1',
              departamentoClave: 'D01',
              departamentoNombre: 'Compras',
              estatus: 0,
              version: 1,
            },
            { status: 201 },
          );
        },
      ),
    );

    render(<SucursalDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText(/no hay departamentos asignados/i)).toBeInTheDocument());

    // Abrir modal de asignación
    const botonAbrir = screen.getByRole('button', { name: /asignar primer departamento/i });
    fireEvent.click(botonAbrir);

    await waitFor(() =>
      expect(
        screen.getByRole('heading', {
          name: /asignar departamento a la sucursal/i,
        }),
      ).toBeInTheDocument(),
    );

    // Seleccionar D01 en el dropdown
    const select = screen.getByLabelText(/departamento a asignar/i);
    fireEvent.change(select, {
      target: { value: 'd-1' },
    });

    // Confirmar en el modal
    const dialog = screen.getByRole('dialog');
    const botonConfirmar = within(dialog).getByRole('button', {
      name: /asignar a sucursal/i,
    });
    fireEvent.click(botonConfirmar);

    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: /desactivar departamento d01/i }),
      ).toBeInTheDocument(),
    );
  });

  it('permite cambiar a la tab Empleados y lista los empleados de la sucursal', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/empleados', () =>
        HttpResponse.json({
          items: [
            {
              id: 'emp-1',
              empresaId: 'e-1',
              clave: 'EMP001',
              nombre: 'Carlos López',
              email: 'carlos@millet.com',
              puestoId: 'p-1',
              puestoNombre: 'Gerente General',
              departamentoId: 'd-1',
              departamentoNombre: 'Dirección',
              sucursalId: 's-1',
              usuarioId: 'u-1',
              estatus: 0,
            },
            {
              id: 'emp-2',
              empresaId: 'e-1',
              clave: 'EMP002',
              nombre: 'María Gómez',
              email: 'maria@millet.com',
              puestoId: null,
              departamentoId: null,
              sucursalId: 's-2', // Otra sucursal
              usuarioId: null,
              estatus: 0,
            },
          ],
          total: 2,
        }),
      ),
    );

    render(<SucursalDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText('S01')).toBeInTheDocument());

    const empleadosTab = screen.getByRole('tab', { name: /^empleados$/i });
    fireEvent.click(empleadosTab);

    await waitFor(() =>
      expect(screen.getByText('Carlos López')).toBeInTheDocument(),
    );
    expect(screen.getByText('EMP001')).toBeInTheDocument();
    expect(screen.getByText(/gerente general/i)).toBeInTheDocument();
    expect(screen.getByText('Acceso ERP')).toBeInTheDocument();
    // María Gómez no debe aparecer porque pertenece a s-2
    expect(screen.queryByText('María Gómez')).not.toBeInTheDocument();
  });

  it('vincula desde la sucursal a un empleado sin sucursal de la empresa activa', async () => {
    setAuth([
      PermisosCanonicos.CompartidoCatalogosLeer,
      PermisosCanonicos.AdminEmpleadosGestionar,
    ]);
    let sucursalAsignada: string | null = null;
    mswServer.use(
      http.get('*/api/v1/catalogos/empleados', () => HttpResponse.json({
        items: [{
          id: 'emp-libre', empresaId: 'e-1', clave: 'EMP003', nombre: 'Lucía Pérez',
          email: 'lucia@millet.com', puestoId: null, departamentoId: null,
          sucursalId: sucursalAsignada, usuarioId: null, estatus: 0,
        }], total: 1,
      })),
      http.patch('*/api/v1/admin/empleados/emp-libre', async ({ request }) => {
        const payload = await request.json() as { sucursalId: string };
        sucursalAsignada = payload.sucursalId;
        return HttpResponse.json({ id: 'emp-libre', sucursalId: sucursalAsignada });
      }),
    );

    render(<SucursalDetalle />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('tab', { name: /^empleados$/i }));
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Empleado sin sucursal' })).toBeInTheDocument());
    fireEvent.change(screen.getByRole('combobox', { name: 'Empleado sin sucursal' }), { target: { value: 'emp-libre' } });
    fireEvent.click(screen.getByRole('button', { name: /vincular a esta sucursal/i }));
    await waitFor(() => expect(sucursalAsignada).toBe('s-1'));
    await waitFor(() => expect(screen.getByText('Lucía Pérez')).toBeInTheDocument());
  });
});
