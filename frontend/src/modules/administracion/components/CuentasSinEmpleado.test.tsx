import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { EmpleadosPage } from '@/modules/administracion/components/EmpleadosPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';

vi.mock('@tanstack/react-router', async (importOriginal) => {
  const original = await importOriginal<typeof import('@tanstack/react-router')>();
  return {
    ...original,
    Link: ({
      children,
      params,
      ...props
    }: {
      children: React.ReactNode;
      params?: { id: string };
      role?: React.AriaRole;
    }) => (
      <a {...props} href={`/admin/empleados/${params?.id ?? ''}`}>
        {children}
      </a>
    ),
  };
});

/**
 * 86e3x6cpw · F1: vista «Cuentas sin empleado» en Empleados. Datos ficticios;
 * el backend se simula con MSW según el contrato 1.
 */
const CUENTAS = {
  items: [
    {
      objectId: 'oid-1',
      nombre: 'Cuenta Ficticia Uno',
      correo: 'ficticia.uno@millet.mx',
      habilitada: true,
      usuarioId: 'u-10',
      yaEntroAlErp: true,
    },
    {
      objectId: 'oid-2',
      nombre: 'Cuenta Ficticia Dos',
      correo: 'ficticia.dos@millet.mx',
      habilitada: true,
      usuarioId: null,
      yaEntroAlErp: false,
    },
    {
      objectId: 'oid-3',
      nombre: 'Cuenta Ficticia Tres',
      correo: 'ficticia.tres@millet.mx',
      habilitada: false,
      usuarioId: null,
      yaEntroAlErp: false,
    },
  ],
  total: 3,
};

const EMPLEADOS = {
  items: [
    {
      id: 'emp-1',
      empresaId: 'e-1',
      clave: 'EMP-001',
      nombre: 'Empleado Sin Cuenta',
      email: null,
      puestoId: 'p-1',
      jefeDirectoId: null,
      sucursalId: 's-1',
      departamentoId: 'd-1',
      usuarioId: null,
      estatus: 0,
    },
    {
      id: 'emp-2',
      empresaId: 'e-1',
      clave: 'EMP-002',
      nombre: 'Empleado Con Cuenta',
      email: 'con.cuenta@millet.mx',
      puestoId: null,
      jefeDirectoId: null,
      sucursalId: null,
      departamentoId: null,
      usuarioId: 'u-2',
      usuarioActivo: true,
      estadoAcceso: 0,
      primerAccesoEn: '2026-10-01T10:00:00Z',
      estatus: 0,
    },
  ],
  offset: 0,
  limit: 200,
  total: 2,
};

const PERMISOS_VISTA = [
  P.AdminEmpleadosGestionar,
  P.IdentidadUsuariosCrear,
  P.AdminEmpleadosGestionarTodasSucursales,
];

const vacio = { items: [], offset: 0, limit: 200, total: 0 };
let busquedas: (string | null)[] = [];

function setPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'admin@millet.mx', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    currentSucursalId: null,
    permisos,
    errorMessage: null,
  });
}

beforeEach(() => {
  busquedas = [];
  mswServer.use(
    http.get('*/api/v1/admin/colaboradores/cuentas-tecnicas', () => HttpResponse.json(vacio)),
    http.get('*/api/v1/catalogos/empleados', () => HttpResponse.json(EMPLEADOS)),
    http.get('*/api/v1/catalogos/puestos', () => HttpResponse.json(vacio)),
    http.get('*/api/v1/catalogos/sucursales', () => HttpResponse.json(vacio)),
    http.get('*/api/v1/catalogos/departamentos', () => HttpResponse.json(vacio)),
    http.get('*/api/v1/identidad/roles*', () =>
      HttpResponse.json({ items: [{ id: 'rol-1', nombre: 'Vendedor', activo: true }] }),
    ),
    http.get('*/api/v1/admin/empleados/siguiente-clave', () =>
      HttpResponse.json({ siguienteClave: 'EMP-003' }),
    ),
    http.get('*/api/v1/admin/colaboradores/cuentas-sin-empleado', ({ request }) => {
      const buscar = new URL(request.url).searchParams.get('buscar');
      busquedas.push(buscar);
      if (!buscar) return HttpResponse.json(CUENTAS);
      const items = CUENTAS.items.filter((c) =>
        c.nombre.toLowerCase().includes(buscar.toLowerCase()),
      );
      return HttpResponse.json({ items, total: items.length });
    }),
  );
});

afterEach(() => {
  sessionStorage.clear();
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

async function abrirVista() {
  render(<EmpleadosPage />, { wrapper: createQueryWrapper() });
  const tab = await screen.findByRole('tab', { name: /Cuentas sin empleado3/ });
  fireEvent.mouseDown(tab);
  fireEvent.click(tab);
  return screen.findByRole('table');
}

describe('Empleados · vista «Cuentas sin empleado» (86e3x6cpw)', () => {
  it('la pestaña aparece con el conteo sólo con los permisos del contrato', async () => {
    setPermisos(PERMISOS_VISTA);
    render(<EmpleadosPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByRole('tab', { name: /Cuentas sin empleado3/ })).toBeInTheDocument();
  });

  it.each([
    [
      'sin identidad.usuarios.crear',
      [P.AdminEmpleadosGestionar, P.AdminEmpleadosGestionarTodasSucursales],
    ],
    ['operativo, sin bypass de sucursal', [P.AdminEmpleadosGestionar, P.IdentidadUsuariosCrear]],
  ])('no aparece %s y no consulta el directorio', async (_, permisos) => {
    setPermisos(permisos);
    render(<EmpleadosPage />, { wrapper: createQueryWrapper() });
    await screen.findByText('Empleado Sin Cuenta');
    expect(screen.queryByRole('tab', { name: /Cuentas sin empleado/ })).not.toBeInTheDocument();
    expect(busquedas).toHaveLength(0);
  });

  it('lista nombre, correo y chips; la deshabilitada no ofrece acciones', async () => {
    setPermisos(PERMISOS_VISTA);
    const lista = await abrirVista();
    const filas = within(lista).getAllByRole('row').slice(1);
    expect(filas).toHaveLength(3);

    expect(within(filas[0]).getByText('ficticia.uno@millet.mx')).toBeInTheDocument();
    expect(within(filas[0]).getByText('Acceso activo')).toBeInTheDocument();
    expect(within(filas[1]).getByText('Sin iniciar sesión')).toBeInTheDocument();
    await abrirAcciones(filas[2]);
    expect(within(filas[2]).getByText('Acceso bloqueado')).toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: /Dar de alta|Ligar/ })).not.toBeInTheDocument();
  });

  it('la cuenta ligada permite abrir su ficha y la cuenta sin usuario ofrece el alta (86e3yxe9m)', async () => {
    setPermisos(PERMISOS_VISTA);
    const lista = await abrirVista();
    const filas = within(lista).getAllByRole('row').slice(1);
    await abrirAcciones(filas[0]);
    expect(screen.getByRole('menuitem', { name: 'Ver cuenta' })).toBeInTheDocument();
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' });
    await abrirAcciones(filas[1]);
    expect(screen.queryByRole('menuitem', { name: 'Ver cuenta' })).not.toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'Dar de alta como empleado' })).toBeInTheDocument();
  });

  it('el buscador compartido consulta las cuentas por nombre tras la pausa', async () => {
    setPermisos(PERMISOS_VISTA);
    await abrirVista();
    fireEvent.change(screen.getByLabelText('Buscar empleados'), { target: { value: 'Dos' } });
    await waitFor(() => expect(busquedas).toContain('Dos'));
    expect(await screen.findByRole('tab', { name: 'Cuentas sin empleado1' })).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText('Cuenta Ficticia Uno')).not.toBeInTheDocument());
    expect(screen.getByText('Cuenta Ficticia Dos')).toBeInTheDocument();
  });

  it('estado vacío cuando todas las cuentas tienen empleado', async () => {
    setPermisos(PERMISOS_VISTA);
    mswServer.use(
      http.get('*/api/v1/admin/colaboradores/cuentas-sin-empleado', () =>
        HttpResponse.json({ items: [], total: 0 }),
      ),
    );
    render(<EmpleadosPage vistaInicial="sin-empleado" />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText('No se encontraron registros con los filtros aplicados'),
    ).toBeInTheDocument();
  });

  it('estado de error con reintento', async () => {
    setPermisos(PERMISOS_VISTA);
    mswServer.use(
      http.get('*/api/v1/admin/colaboradores/cuentas-sin-empleado', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Error del directorio', status: 503 },
          { status: 503 },
        ),
      ),
    );
    render(<EmpleadosPage vistaInicial="sin-empleado" />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText('No se pudieron consultar las cuentas', {}, { timeout: 4000 }),
    ).toBeInTheDocument();
  });

  it('«Crear empleado con esta cuenta» abre el alta con el nombre de la cuenta', async () => {
    setPermisos(PERMISOS_VISTA);
    const lista = await abrirVista();
    const fila = within(lista).getAllByRole('row').slice(1)[1];
    await abrirAcciones(fila);
    fireEvent.click(screen.getByRole('menuitem', { name: 'Dar de alta como empleado' }));

    const form = await screen.findByRole('form', { name: /agregar empleado/i });
    expect(within(form).getByPlaceholderText('Juana Pérez')).toHaveValue('Cuenta Ficticia Dos');
  });

  it('«Ligar a empleado existente» da acceso con la cuenta existente al empleado elegido', async () => {
    setPermisos(PERMISOS_VISTA);
    let body: Record<string, unknown> | null = null;
    let url = '';
    mswServer.use(
      http.post('*/api/v1/admin/colaboradores/:id/acceso', async ({ request }) => {
        url = request.url;
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({
          empleado: { id: 'emp-1' },
          acceso: { usuarioId: 'u-10', estadoAcceso: 0 },
        });
      }),
    );
    const lista = await abrirVista();
    const fila = within(lista).getAllByRole('row').slice(1)[0];
    await abrirAcciones(fila);
    fireEvent.click(screen.getByRole('menuitem', { name: 'Ligar a empleado existente' }));
    const dialogo = await screen.findByRole('dialog');

    const selectEmpleado = await within(dialogo).findByLabelText('Empleado sin cuenta');
    // Sólo empleados activos sin cuenta.
    expect(within(selectEmpleado).queryByText(/Empleado Con Cuenta/)).not.toBeInTheDocument();
    await within(selectEmpleado).findByText(/EMP-001 · Empleado Sin Cuenta/);
    fireEvent.change(selectEmpleado, { target: { value: 'emp-1' } });
    const selectRol = within(dialogo).getByLabelText('Rol en la empresa');
    await within(selectRol).findByText('Vendedor');
    fireEvent.change(selectRol, { target: { value: 'rol-1' } });
    fireEvent.click(within(dialogo).getByRole('button', { name: 'Ligar cuenta' }));

    await waitFor(() => expect(body).not.toBeNull());
    expect(url).toContain('/api/v1/admin/colaboradores/emp-1/acceso');
    expect(body).toMatchObject({
      acceso: 1,
      correoCorporativo: 'ficticia.uno@millet.mx',
      rolId: 'rol-1',
      departamentoId: 'd-1',
      puestoId: 'p-1',
    });
  });
});

async function abrirAcciones(fila: HTMLElement) {
  fireEvent.keyDown(within(fila).getByRole('button', { name: /Acciones de/ }), {
    key: 'ArrowDown',
  });
  await screen.findByRole('menu');
}
