import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse, delay } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { EmpleadosPage } from '@/modules/administracion/components/EmpleadosPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

vi.mock('@tanstack/react-router', async (importOriginal) => {
  const original = await importOriginal<typeof import('@tanstack/react-router')>();
  return {
    ...original,
    Link: ({
      children,
      params,
      className,
      ...props
    }: React.ComponentProps<'a'> & { params: { id: string } }) => (
      <a {...props} href={`/admin/empleados/${params.id}`} className={className}>
        {children}
      </a>
    ),
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
    currentSucursalId: null,
    permisos,
    errorMessage: null,
  });
}

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/catalogos/empleados', () => HttpResponse.json(EMPLEADOS)),
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
    currentSucursalId: null,
    permisos: [],
    errorMessage: null,
  });
});

describe('<EmpleadosPage> — smoke (ADM-FE-PR1)', () => {
  it('renderiza la lista con email, clave de puesto resuelta y badges', async () => {
    setPermisos([PermisosCanonicos.AdminEmpleadosGestionar]);
    render(<EmpleadosPage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Juana Pérez')).toBeInTheDocument();
    expect(screen.getByText(/juana.perez@millet.mx/)).toBeInTheDocument();
    expect(screen.getByText('Pedro López')).toBeInTheDocument();
    // Columna Puesto: id resuelto a clave vía GET /catalogos/puestos.
    expect(await screen.findByText('GER')).toBeInTheDocument();
    expect(screen.getByText(/EMP-002/).closest('p')).toHaveTextContent('Inactivo');
    expect(screen.getByText('Acceso activo')).toBeInTheDocument();
  });

  it('con permiso: "Agregar empleado" monta el inline form; el inactivo ofrece Reactivar', async () => {
    setPermisos([PermisosCanonicos.AdminEmpleadosGestionar]);
    render(<EmpleadosPage />, { wrapper: createQueryWrapper() });

    await screen.findByText('Juana Pérez');
    fireEvent.click(screen.getByRole('button', { name: /agregar empleado/i }));
    expect(screen.getByRole('form', { name: /agregar empleado/i })).toBeInTheDocument();
    expect(screen.getByText(/Paso 1 de 4:/i)).toBeInTheDocument();
    expect(screen.getByText('Persona y sucursal')).toBeInTheDocument();

    await abrirAcciones('Pedro López');
    expect(
      screen.getByRole('menuitem', { name: /reactivar empleado EMP-002/i }),
    ).toBeInTheDocument();
  });

  it('sin permiso de gestión: lista visible pero sin acciones de CRUD', async () => {
    setPermisos([]);
    render(<EmpleadosPage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Juana Pérez')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /agregar empleado/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /editar empleado/i })).not.toBeInTheDocument();
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
      expect(screen.getByText(/No se pudieron cargar los empleados/i)).toBeInTheDocument(),
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
    fireEvent.mouseDown(screen.getByRole('tab', { name: /^sin acceso\d+$/i }), {
      button: 0,
      ctrlKey: false,
    });
    expect(screen.getByText('Juana Pérez')).toBeInTheDocument();
    expect(screen.queryByText('Ana Pendiente Login')).not.toBeInTheDocument();
  });

  it('permite reenviar acceso al colaborador pendiente de primer login', async () => {
    let reenvioLlamado = false;
    mswServer.use(
      http.post('*/api/v1/admin/colaboradores/:id/acceso/reenviar', async ({ params }) => {
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
      }),
    );

    setPermisos([
      PermisosCanonicos.AdminEmpleadosGestionar,
      PermisosCanonicos.IdentidadUsuariosCrear,
    ]);
    render(<EmpleadosPage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Ana Pendiente Login')).toBeInTheDocument();
    await abrirAcciones('Ana Pendiente Login');
    const btnReenviar = screen.getByRole('menuitem', { name: /reenviar acceso/i });
    expect(btnReenviar).toBeInTheDocument();

    fireEvent.click(btnReenviar);

    await waitFor(() => {
      expect(reenvioLlamado).toBe(true);
    });
  });

  it('filtra por "Por reasignar" y muestra banner informativo y botón de reasignar', async () => {
    setPermisos([PermisosCanonicos.AdminEmpleadosGestionar]);
    render(<EmpleadosPage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Juana Pérez')).toBeInTheDocument();

    const btnPorReasignar = screen.getByRole('button', { name: /por reasignar/i });
    expect(btnPorReasignar).toBeInTheDocument();
    fireEvent.click(btnPorReasignar);

    expect(screen.getByText('Colaboradores pendientes de reasignación')).toBeInTheDocument();
    expect(
      screen.getByText(/Los colaboradores continúan operando con normalidad con su histórico/i),
    ).toBeInTheDocument();

    // Empleados activos sin sucursal/depto aparecen y ofrecen botón Reasignar
    expect(screen.getByText('Juana Pérez')).toBeInTheDocument();
    const reasignarBtns = screen.getAllByRole('button', { name: /reasignar/i });
    expect(reasignarBtns.length).toBeGreaterThan(0);
  });
  it('"Dar acceso" exige usuarios.crear y asignaciones.administrar', async () => {
    setPermisos([
      PermisosCanonicos.AdminEmpleadosGestionar,
      PermisosCanonicos.IdentidadUsuariosCrear,
    ]);
    const { unmount } = render(<EmpleadosPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Juana Pérez')).toBeInTheDocument();
    await abrirAcciones('Juana Pérez');
    expect(screen.queryByRole('menuitem', { name: /dar acceso/i })).not.toBeInTheDocument();
    unmount();

    setPermisos([
      PermisosCanonicos.AdminEmpleadosGestionar,
      PermisosCanonicos.IdentidadUsuariosCrear,
      PermisosCanonicos.IdentidadAsignacionesAdministrar,
    ]);
    render(<EmpleadosPage />, { wrapper: createQueryWrapper() });
    await screen.findByText('Juana Pérez');
    await abrirAcciones('Juana Pérez');
    expect(screen.getByRole('menuitem', { name: /dar acceso/i })).toBeInTheDocument();
  });
});

it('no presenta ceros de empleados mientras los indicadores todavía cargan', async () => {
  mswServer.use(
    http.get('*/api/v1/catalogos/empleados', async () => {
      await delay(100);
      return HttpResponse.json(EMPLEADOS);
    }),
  );
  render(<EmpleadosPage />, { wrapper: createQueryWrapper() });
  const indicador = screen.getByRole('button', { name: /Sin iniciar sesión —/ });
  expect(indicador).toBeInTheDocument();
  expect(indicador.querySelector('.text-3xl')).toHaveClass('font-semibold', 'tabular-nums');
  await screen.findByText('Juana Pérez');
  expect(screen.queryByRole('button', { name: /Sin iniciar sesión —/ })).not.toBeInTheDocument();
});

async function abrirAcciones(nombre: string) {
  fireEvent.keyDown(screen.getByRole('button', { name: `Acciones de ${nombre}` }), {
    key: 'ArrowDown',
  });
  await screen.findByRole('menu');
}

const PERMISOS_CUENTAS = [
  PermisosCanonicos.AdminEmpleadosGestionar,
  PermisosCanonicos.IdentidadUsuariosCrear,
  PermisosCanonicos.AdminEmpleadosGestionarTodasSucursales,
];
const CUENTA = {
  objectId: 'cuenta-1',
  nombre: 'Cuenta directorio',
  correo: 'directorio@example.test',
  habilitada: true,
  usuarioId: null,
  yaEntroAlErp: false,
};
const TECNICA = {
  usuarioId: 'tecnica-1',
  nombre: 'Cuenta soporte',
  correo: 'soporte@example.test',
  roles: ['Soporte'],
  activo: true,
  estadoAcceso: 0,
  primerAccesoEn: '2026-09-10T10:00:00Z',
};
function prepararCuentas() {
  setPermisos(PERMISOS_CUENTAS);
  mswServer.use(
    http.get('*/api/v1/admin/colaboradores/cuentas-sin-empleado', () =>
      HttpResponse.json({ items: [CUENTA], total: 1 }),
    ),
    http.get('*/api/v1/admin/colaboradores/cuentas-tecnicas', () =>
      HttpResponse.json({ items: [TECNICA], total: 1 }),
    ),
  );
}
function elegirTab(nombre: RegExp) {
  const tab = screen.getByRole('tab', { name: nombre });
  fireEvent.mouseDown(tab, { button: 0, ctrlKey: false });
  return tab;
}
function filas() {
  return within(screen.getByRole('table')).getAllByRole('row').slice(1);
}

it('Todos incluye las tres fuentes y los conteos coinciden con cada vista aunque se solapen', async () => {
  prepararCuentas();
  render(<EmpleadosPage />, { wrapper: createQueryWrapper() });
  await screen.findByText(CUENTA.nombre);
  expect(filas()).toHaveLength(6);
  expect(screen.getByRole('tab', { name: /^Todos6$/ })).toHaveAttribute('aria-selected', 'true');
  for (const [nombre, cantidad] of [
    [/^Con acceso ERP2$/, 2],
    [/^Sin iniciar sesión1$/, 1],
    [/^Sin acceso2$/, 2],
    [/^Acceso bloqueado0$/, 0],
    [/^Por reasignar3$/, 3],
    [/^Cuentas sin empleado1$/, 1],
    [/^Cuentas técnicas1$/, 1],
  ] as const) {
    elegirTab(nombre);
    if (cantidad) expect(filas()).toHaveLength(cantidad);
    else expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.getByText(`${cantidad} registros`)).toBeInTheDocument();
  }
  fireEvent.click(screen.getByRole('button', { name: /^Sin acceso ERP 2$/ }));
  expect(screen.getByRole('tab', { name: /^Sin acceso2$/ })).toHaveAttribute(
    'aria-selected',
    'true',
  );
  expect(filas()).toHaveLength(2);
});

it('búsqueda y filtros actualizan cuentas, empleados, conteos y KPIs', async () => {
  prepararCuentas();
  mswServer.use(
    http.get('*/api/v1/catalogos/sucursales', () =>
      HttpResponse.json({ items: [{ id: 's-1', nombre: 'Planta', tipo: 0 }], total: 1 }),
    ),
  );
  render(<EmpleadosPage />, { wrapper: createQueryWrapper() });
  await screen.findByText(CUENTA.nombre);
  fireEvent.change(screen.getByRole('textbox', { name: 'Buscar empleados' }), {
    target: { value: 'directorio@' },
  });
  await screen.findByRole('tab', { name: /^Todos1$/ });
  expect(filas()).toHaveLength(1);
  expect(screen.getByRole('tab', { name: /^Todos1$/ })).toBeInTheDocument();
  fireEvent.change(screen.getByRole('combobox', { name: 'Filtrar por sucursal' }), {
    target: { value: 's-1' },
  });
  expect(screen.queryByText(CUENTA.nombre)).not.toBeInTheDocument();
  expect(screen.getByRole('tab', { name: /^Cuentas sin empleado0$/ })).toBeInTheDocument();
  expect(
    screen.getByText('No se encontraron registros con los filtros aplicados'),
  ).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
  fireEvent.change(screen.getByRole('textbox', { name: 'Buscar empleados' }), {
    target: { value: 'GER' },
  });
  await screen.findByText('Juana Pérez');
  expect(filas()).toHaveLength(1);
  expect(screen.getByText('Juana Pérez')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: /^Sin acceso ERP 1$/ })).toBeInTheDocument();
});

it('con cero empleados, Todos muestra las cuentas y conserva sus acciones', async () => {
  prepararCuentas();
  mswServer.use(
    http.get('*/api/v1/catalogos/empleados', () => HttpResponse.json({ items: [], total: 0 })),
  );
  render(<EmpleadosPage />, { wrapper: createQueryWrapper() });
  await screen.findByText(CUENTA.nombre);
  expect(filas()).toHaveLength(2);
  expect(screen.queryByText(/No se encontraron registros/)).not.toBeInTheDocument();
  await abrirAcciones(CUENTA.nombre);
  fireEvent.click(screen.getByRole('menuitem', { name: 'Dar de alta como empleado' }));
  expect(screen.getByRole('form', { name: /Agregar empleado/i })).toBeInTheDocument();
});

it('pagina todas las vistas a diez filas y reinicia la página al cambiar filtros', async () => {
  prepararCuentas();
  mswServer.use(
    http.get('*/api/v1/admin/colaboradores/cuentas-sin-empleado', () =>
      HttpResponse.json({
        items: Array.from({ length: 12 }, (_, i) => ({
          ...CUENTA,
          objectId: `c-${i}`,
          nombre: `Cuenta ${i}`,
        })),
        total: 12,
      }),
    ),
  );
  render(<EmpleadosPage vistaInicial="sin-empleado" />, { wrapper: createQueryWrapper() });
  await screen.findByText('Cuenta 0');
  expect(filas()).toHaveLength(10);
  fireEvent.click(screen.getByRole('button', { name: /Siguiente/i }));
  expect(filas()).toHaveLength(2);
  expect(screen.getByText('Cuenta 10')).toBeInTheDocument();
  elegirTab(/^Todos17$/);
  expect(filas()).toHaveLength(10);
  expect(screen.getByText('Juana Pérez')).toBeInTheDocument();
});

it('oculta los conteos hasta terminar las tres consultas', async () => {
  prepararCuentas();
  mswServer.use(
    http.get('*/api/v1/admin/colaboradores/cuentas-tecnicas', async () => {
      await delay(200);
      return HttpResponse.json({ items: [TECNICA], total: 1 });
    }),
  );
  render(<EmpleadosPage />, { wrapper: createQueryWrapper() });
  expect(screen.getByRole('tab', { name: 'Todos' })).toBeInTheDocument();
  expect(screen.getByRole('tab', { name: 'Cuentas técnicas' })).toBeInTheDocument();
  await screen.findByRole('tab', { name: 'Todos6' });
});

it('cada KPI selecciona su pestaña desde una vista de cuentas', async () => {
  prepararCuentas();
  render(<EmpleadosPage vistaInicial="tecnicas" />, { wrapper: createQueryWrapper() });
  await screen.findByText(TECNICA.nombre);
  for (const [kpi, tab] of [
    [/^Sin iniciar sesión 1$/, /^Sin iniciar sesión1$/],
    [/^Sin acceso ERP 2$/, /^Sin acceso2$/],
    [/^Acceso bloqueado 0$/, /^Acceso bloqueado0$/],
    [/^Por reasignar 3$/, /^Por reasignar3$/],
  ]) {
    elegirTab(/^Cuentas técnicas1$/);
    fireEvent.click(screen.getByRole('button', { name: kpi }));
    expect(screen.getByRole('tab', { name: tab })).toHaveAttribute('aria-selected', 'true');
    expect(screen.queryByText(TECNICA.nombre)).not.toBeInTheDocument();
  }
});

it('sin permiso corporativo no consulta ni incluye cuentas, incluso en vistaInicial técnica', async () => {
  setPermisos([PermisosCanonicos.AdminEmpleadosGestionar]);
  const consultas = vi.fn();
  mswServer.use(
    http.get('*/api/v1/admin/colaboradores/cuentas-sin-empleado', () => {
      consultas();
      return HttpResponse.json({ items: [CUENTA], total: 1 });
    }),
    http.get('*/api/v1/admin/colaboradores/cuentas-tecnicas', () => {
      consultas();
      return HttpResponse.json({ items: [TECNICA], total: 1 });
    }),
  );
  render(<EmpleadosPage vistaInicial="tecnicas" />, { wrapper: createQueryWrapper() });
  await screen.findByText('Juana Pérez');
  expect(filas()).toHaveLength(4);
  expect(screen.getByRole('tab', { name: /^Todos4$/ })).toBeInTheDocument();
  expect(screen.queryByRole('tab', { name: /Cuentas/ })).not.toBeInTheDocument();
  expect(consultas).not.toHaveBeenCalled();
});

it('departamento conserva solo empleados coincidentes y bloqueados usa usuarioActivo=false', async () => {
  prepararCuentas();
  mswServer.use(
    http.get('*/api/v1/catalogos/empleados', () =>
      HttpResponse.json({
        items: [{ ...EMPLEADOS.items[2], departamentoId: 'd-1', usuarioActivo: false }],
        total: 1,
      }),
    ),
    http.get('*/api/v1/catalogos/departamentos', () =>
      HttpResponse.json({ items: [{ id: 'd-1', nombre: 'Compras', estatus: 0 }], total: 1 }),
    ),
  );
  render(<EmpleadosPage />, { wrapper: createQueryWrapper() });
  await screen.findByText(CUENTA.nombre);
  fireEvent.change(screen.getByRole('combobox', { name: 'Filtrar por departamento' }), {
    target: { value: 'd-1' },
  });
  expect(filas()).toHaveLength(1);
  expect(screen.getByRole('tab', { name: /^Todos1$/ })).toBeInTheDocument();
  expect(screen.getByRole('tab', { name: /^Cuentas técnicas0$/ })).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: /^Acceso bloqueado 1$/ }));
  expect(screen.getByRole('tab', { name: /^Acceso bloqueado1$/ })).toHaveAttribute(
    'aria-selected',
    'true',
  );
  expect(within(filas()[0]).getByText('Acceso bloqueado')).toBeInTheDocument();
});

it('muestra el rol y el primer acceso de una cuenta técnica en sus acciones', async () => {
  prepararCuentas();
  render(<EmpleadosPage vistaInicial="tecnicas" />, { wrapper: createQueryWrapper() });
  await screen.findByText(TECNICA.nombre);
  await abrirAcciones(TECNICA.nombre);
  expect(screen.getByText('Rol: Soporte')).toBeInTheDocument();
  expect(screen.getByText(/Primer acceso:/)).toBeInTheDocument();
});

it('avisa si el contrato de cuentas devuelve una consulta limitada y busca también en el servidor', async () => {
  prepararCuentas();
  const busquedas: (string | null)[] = [];
  mswServer.use(
    http.get('*/api/v1/admin/colaboradores/cuentas-sin-empleado', ({ request }) => {
      const buscar = new URL(request.url).searchParams.get('buscar');
      busquedas.push(buscar);
      return HttpResponse.json({
        items: [{ ...CUENTA, nombre: buscar ? 'Otra persona' : CUENTA.nombre }],
        total: buscar ? 1 : 51,
      });
    }),
  );
  render(<EmpleadosPage />, { wrapper: createQueryWrapper() });
  expect(await screen.findByText(/La consulta de cuentas alcanzó su límite/)).toBeInTheDocument();
  fireEvent.change(screen.getByRole('textbox', { name: 'Buscar empleados' }), {
    target: { value: 'Otra persona' },
  });
  await screen.findByText('Otra persona');
  expect(busquedas).toContain('Otra persona');
  expect(screen.queryByText(/La consulta de cuentas alcanzó su límite/)).not.toBeInTheDocument();
  expect(screen.getByRole('tab', { name: /^Todos1$/ })).toBeInTheDocument();
});

it('una consulta de cuentas fallida no presenta un total parcial como Todos ni un falso vacío', async () => {
  prepararCuentas();
  mswServer.use(
    http.get('*/api/v1/admin/colaboradores/cuentas-tecnicas', () =>
      HttpResponse.json({ title: 'Error del directorio', status: 503 }, { status: 503 }),
    ),
  );
  render(<EmpleadosPage />, { wrapper: createQueryWrapper() });
  await screen.findByText('No se pudieron consultar las cuentas');
  expect(screen.getByRole('tab', { name: 'Todos' })).toBeInTheDocument();
  expect(screen.getByText('Consulta incompleta')).toBeInTheDocument();
  expect(screen.queryByText(/No se encontraron registros/)).not.toBeInTheDocument();
});
