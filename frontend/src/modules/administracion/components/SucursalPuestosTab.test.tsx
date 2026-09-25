import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SucursalPuestosTab } from '@/modules/administracion/components/SucursalPuestosTab';
import { useAuthStore } from '@/lib/auth/auth-store';

const SUCURSAL_ID = '00000000-0000-0000-0000-000000000010';
const DEPTO_DIR = '00000000-0000-0000-0000-000000000099';
const DEPTO_ADM = '00000000-0000-0000-0000-000000000088';
const ROL_ADMIN = '00000000-0000-0000-0000-000000000c01';

const ROLES = {
  items: [
    { id: ROL_ADMIN, codigo: 'ADMIN', nombre: 'Administrador', activo: true, permisoIds: [] },
  ],
  offset: 0,
  limit: 200,
  total: 1,
};

const CATALOGO_PUESTOS = {
  items: [
    {
      id: '00000000-0000-0000-0000-000000000001',
      clave: 'GER',
      nombre: 'Gerente General',
      estatus: 0,
      departamentoId: DEPTO_DIR,
      departamentoNombre: 'Dirección',
    },
    {
      id: '00000000-0000-0000-0000-000000000002',
      clave: 'AUX',
      nombre: 'Auxiliar Administrativo',
      estatus: 0,
      departamentoId: null,
      departamentoNombre: null,
    },
  ],
  offset: 0,
  limit: 200,
  total: 2,
};

const DEPARTAMENTOS_DE_SUCURSAL = {
  items: [
    {
      sucursalId: SUCURSAL_ID,
      departamentoId: DEPTO_DIR,
      departamentoClave: 'DIR',
      departamentoNombre: 'Dirección',
      estatus: 0,
      version: 1,
    },
    {
      sucursalId: SUCURSAL_ID,
      departamentoId: DEPTO_ADM,
      departamentoClave: 'ADM',
      departamentoNombre: 'Administración',
      estatus: 0,
      version: 1,
    },
  ],
  total: 2,
};

// Un puesto (GER) con DOS asignaciones — Dirección y Administración —
// para probar el agrupamiento "un puesto, varios departamentos".
const ASIGNADOS_INICIALES = {
  items: [
    {
      sucursalId: SUCURSAL_ID,
      puestoId: '00000000-0000-0000-0000-000000000001',
      puestoClave: 'GER',
      puestoNombre: 'Gerente General',
      departamentoId: DEPTO_DIR,
      departamentoNombre: 'Dirección',
      rolSugeridoId: null,
      rolSugeridoEfectivoId: ROL_ADMIN,
      estatus: 0,
      version: 1,
    },
    {
      sucursalId: SUCURSAL_ID,
      puestoId: '00000000-0000-0000-0000-000000000001',
      puestoClave: 'GER',
      puestoNombre: 'Gerente General',
      departamentoId: DEPTO_ADM,
      departamentoNombre: 'Administración',
      rolSugeridoId: null,
      rolSugeridoEfectivoId: ROL_ADMIN,
      estatus: 0,
      version: 1,
    },
  ],
  total: 2,
};

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: ['admin.sucursales.puestos-gestionar'],
    errorMessage: null,
  });

  mswServer.use(
    http.get('*/api/v1/catalogos/puestos', () =>
      HttpResponse.json(CATALOGO_PUESTOS),
    ),
    http.get(
      '*/api/v1/admin/empresas/sucursales/:sucursalId/departamentos',
      () => HttpResponse.json(DEPARTAMENTOS_DE_SUCURSAL),
    ),
    http.get(
      '*/api/v1/admin/empresas/sucursales/:sucursalId/puestos',
      () => HttpResponse.json(ASIGNADOS_INICIALES),
    ),
    http.get('*/api/v1/identidad/roles', () => HttpResponse.json(ROLES)),
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

describe('<SucursalPuestosTab>', () => {
  it('agrupa las asignaciones de un mismo puesto en varios departamentos', async () => {
    render(<SucursalPuestosTab sucursalId={SUCURSAL_ID} canGestionar={true} />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => {
      expect(screen.getByText('GER')).toBeInTheDocument();
    });

    // El puesto GER aparece UNA sola vez agrupado (no repetido por fila)
    expect(screen.getAllByText('GER')).toHaveLength(1);
    // Con sus dos departamentos listados debajo
    expect(screen.getByText('Dirección')).toBeInTheDocument();
    expect(screen.getByText('Administración')).toBeInTheDocument();
    expect(screen.getByText('2 departamentos')).toBeInTheDocument();

    // El puesto sin asignar no debe aparecer en la lista
    expect(screen.queryByText('Auxiliar Administrativo')).not.toBeInTheDocument();
  });

  it('muestra "Hereda del puesto" cuando la asignación no tiene excepción de rol sugerido', async () => {
    render(<SucursalPuestosTab sucursalId={SUCURSAL_ID} canGestionar={true} />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(screen.getByText('GER')).toBeInTheDocument());

    expect(screen.getAllByText(/hereda del puesto/i)).toHaveLength(2);
  });

  it('abre el modal, permite elegir varios departamentos y hace una llamada por cada uno', async () => {
    const departamentosRecibidos: string[] = [];

    mswServer.use(
      http.post(
        '*/api/v1/admin/empresas/sucursales/:sucursalId/puestos/:puestoId',
        async ({ request }) => {
          const body = (await request.json()) as { departamentoId: string };
          departamentosRecibidos.push(body.departamentoId);
          return HttpResponse.json(
            {
              sucursalId: SUCURSAL_ID,
              puestoId: '00000000-0000-0000-0000-000000000002',
              puestoClave: 'AUX',
              puestoNombre: 'Auxiliar Administrativo',
              departamentoId: body.departamentoId,
              departamentoNombre: body.departamentoId === DEPTO_DIR ? 'Dirección' : 'Administración',
              rolSugeridoId: null,
              rolSugeridoEfectivoId: null,
              estatus: 0,
              version: 1,
            },
            { status: 201 },
          );
        },
      ),
    );

    render(<SucursalPuestosTab sucursalId={SUCURSAL_ID} canGestionar={true} />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(screen.getByText('GER')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: /^asignar puesto$/i }));

    await waitFor(() =>
      expect(
        screen.getByRole('heading', {
          name: /asignar puesto a departamentos de la sucursal/i,
        }),
      ).toBeInTheDocument(),
    );

    const selectPuesto = screen.getByLabelText(/puesto a asignar/i);
    fireEvent.change(selectPuesto, {
      target: { value: '00000000-0000-0000-0000-000000000002' },
    });

    const checkboxDir = await screen.findByLabelText('Departamento DIR');
    const checkboxAdm = screen.getByLabelText('Departamento ADM');
    fireEvent.click(checkboxDir);
    fireEvent.click(checkboxAdm);

    const dialog = screen.getByRole('dialog');
    const botonConfirmar = within(dialog).getByRole('button', {
      name: /^asignar puesto$/i,
    });
    fireEvent.click(botonConfirmar);

    await waitFor(() => expect(departamentosRecibidos).toHaveLength(2));
    expect(departamentosRecibidos.sort()).toEqual([DEPTO_ADM, DEPTO_DIR].sort());
  });

  it('excluye del selector de departamentos los que el puesto ya tiene asignados', async () => {
    render(<SucursalPuestosTab sucursalId={SUCURSAL_ID} canGestionar={true} />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(screen.getByText('GER')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: /^asignar puesto$/i }));
    await waitFor(() =>
      expect(
        screen.getByRole('heading', {
          name: /asignar puesto a departamentos de la sucursal/i,
        }),
      ).toBeInTheDocument(),
    );

    // GER ya está en DIR y ADM (los dos únicos departamentos activos)
    const selectPuesto = screen.getByLabelText(/puesto a asignar/i);
    fireEvent.change(selectPuesto, {
      target: { value: '00000000-0000-0000-0000-000000000001' },
    });

    await waitFor(() =>
      expect(
        screen.getByText(/ya está asignado a todos los departamentos activos/i),
      ).toBeInTheDocument(),
    );
  });

  it('muestra mensaje informativo y deshabilita confirmación cuando la sucursal no tiene departamentos asignados', async () => {
    mswServer.use(
      http.get(
        '*/api/v1/admin/empresas/sucursales/:sucursalId/departamentos',
        () => HttpResponse.json({ items: [], total: 0 }),
      ),
      http.get(
        '*/api/v1/admin/empresas/sucursales/:sucursalId/puestos',
        () => HttpResponse.json({ items: [], total: 0 }),
      ),
    );

    render(<SucursalPuestosTab sucursalId={SUCURSAL_ID} canGestionar={true} />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() =>
      expect(screen.getByText(/no hay puestos asignados/i)).toBeInTheDocument(),
    );

    const botonAsignar = screen.getByRole('button', {
      name: /asignar primer puesto/i,
    });
    fireEvent.click(botonAsignar);

    await waitFor(() =>
      expect(
        screen.getByText(/esta sucursal no tiene departamentos activos asignados/i),
      ).toBeInTheDocument(),
    );

    const botonConfirmar = screen.getByRole('button', {
      name: /^asignar puesto$/i,
    });
    expect(botonConfirmar).toBeDisabled();
  });

  it('edita el rol sugerido de una asignación con el inline form (patrón amber)', async () => {
    let bodyVisto: unknown = null;

    mswServer.use(
      http.patch(
        '*/api/v1/admin/empresas/sucursales/:sucursalId/puestos/:puestoId/departamentos/:departamentoId',
        async ({ request }) => {
          bodyVisto = await request.json();
          return HttpResponse.json({
            sucursalId: SUCURSAL_ID,
            puestoId: '00000000-0000-0000-0000-000000000001',
            puestoClave: 'GER',
            puestoNombre: 'Gerente General',
            departamentoId: DEPTO_DIR,
            departamentoNombre: 'Dirección',
            rolSugeridoId: ROL_ADMIN,
            rolSugeridoEfectivoId: ROL_ADMIN,
            estatus: 0,
            version: 2,
          });
        },
      ),
    );

    render(<SucursalPuestosTab sucursalId={SUCURSAL_ID} canGestionar={true} />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(screen.getByText('GER')).toBeInTheDocument());

    const botonesEditar = screen.getAllByRole('button', { name: /editar rol sugerido/i });
    fireEvent.click(botonesEditar[0]);

    const select = await screen.findByLabelText(/rol sugerido para ger en/i);
    fireEvent.change(select, { target: { value: ROL_ADMIN } });

    fireEvent.click(screen.getByRole('button', { name: /^guardar$/i }));

    await waitFor(() => expect(bodyVisto).toEqual({ rolSugeridoId: ROL_ADMIN }));
  });
});
