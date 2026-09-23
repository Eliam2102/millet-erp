import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SucursalOrganizacionTab } from '@/modules/administracion/components/SucursalOrganizacionTab';
import { useAuthStore } from '@/lib/auth/auth-store';

const SUCURSAL_ID = '00000000-0000-0000-0000-000000000010';
const DEPTO_DIR = '00000000-0000-0000-0000-000000000099';
const DEPTO_ING = '00000000-0000-0000-0000-000000000088';

const CATALOGO_DEPARTAMENTOS = {
  items: [
    { id: DEPTO_DIR, clave: 'DIR', nombre: 'Dirección General', estatus: 0, version: 1 },
    { id: DEPTO_ING, clave: 'ING', nombre: 'Ingeniería', estatus: 0, version: 1 },
    { id: '00000000-0000-0000-0000-000000000077', clave: 'RH', nombre: 'Recursos Humanos', estatus: 0, version: 1 },
  ],
  offset: 0,
  limit: 200,
  total: 3,
};

const CATALOGO_PUESTOS = {
  items: [
    { id: '00000000-0000-0000-0000-000000000001', clave: 'GER', nombre: 'Gerente General', estatus: 0 },
    { id: '00000000-0000-0000-0000-000000000002', clave: 'ING_PLANT', nombre: 'Ingeniero de Planta', estatus: 0 },
    { id: '00000000-0000-0000-0000-000000000003', clave: 'AUX', nombre: 'Auxiliar General', estatus: 0 },
  ],
  offset: 0,
  limit: 200,
  total: 3,
};

const DEPARTAMENTOS_ASIGNADOS = {
  items: [
    {
      sucursalId: SUCURSAL_ID,
      departamentoId: DEPTO_DIR,
      departamentoClave: 'DIR',
      departamentoNombre: 'Dirección General',
      estatus: 0,
      version: 1,
    },
    {
      sucursalId: SUCURSAL_ID,
      departamentoId: DEPTO_ING,
      departamentoClave: 'ING',
      departamentoNombre: 'Ingeniería',
      estatus: 0,
      version: 1,
    },
  ],
  total: 2,
};

const PUESTOS_ASIGNADOS = {
  items: [
    {
      sucursalId: SUCURSAL_ID,
      puestoId: '00000000-0000-0000-0000-000000000001',
      puestoClave: 'GER',
      puestoNombre: 'Gerente General',
      departamentoId: DEPTO_DIR,
      departamentoNombre: 'Dirección General',
      estatus: 0,
      version: 1,
    },
    {
      sucursalId: SUCURSAL_ID,
      puestoId: '00000000-0000-0000-0000-000000000002',
      puestoClave: 'ING_PLANT',
      puestoNombre: 'Ingeniero de Planta',
      departamentoId: DEPTO_ING,
      departamentoNombre: 'Ingeniería',
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
    permisos: [
      'admin.sucursales.departamentos-gestionar',
      'admin.sucursales.puestos-gestionar',
    ],
    errorMessage: null,
  });

  mswServer.use(
    http.get('*/api/v1/catalogos/departamentos', () =>
      HttpResponse.json(CATALOGO_DEPARTAMENTOS),
    ),
    http.get('*/api/v1/catalogos/puestos', () =>
      HttpResponse.json(CATALOGO_PUESTOS),
    ),
    http.get('*/api/v1/admin/empresas/sucursales/:sucursalId/departamentos', () =>
      HttpResponse.json(DEPARTAMENTOS_ASIGNADOS),
    ),
    http.get('*/api/v1/admin/empresas/sucursales/:sucursalId/puestos', () =>
      HttpResponse.json(PUESTOS_ASIGNADOS),
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

describe('<SucursalOrganizacionTab>', () => {
  it('renderiza departamentos arriba y filtra puestos del departamento seleccionado abajo', async () => {
    render(
      <SucursalOrganizacionTab
        sucursalId={SUCURSAL_ID}
        canGestionarDeptos={true}
        canGestionarPuestos={true}
      />,
      { wrapper: createQueryWrapper() },
    );

    // Arriba: deben aparecer ambos departamentos asignados
    await waitFor(() => {
      expect(screen.getByText('DIR')).toBeInTheDocument();
      expect(screen.getByText('Dirección General')).toBeInTheDocument();
      expect(screen.getByText('ING')).toBeInTheDocument();
      expect(screen.getByText('Ingeniería')).toBeInTheDocument();
    });

    // Abajo por defecto está seleccionado el primer departamento (DIR)
    expect(screen.getByText(/puestos de dir — dirección general/i)).toBeInTheDocument();
    // En la sección de puestos debe estar el puesto de DIR (GER)
    expect(screen.getByText('Gerente General')).toBeInTheDocument();
    // Y NO debe estar el puesto de ING en esta lista
    expect(screen.queryByText('Ingeniero de Planta')).not.toBeInTheDocument();

    // Ahora hacemos clic en el departamento ING para seleccionarlo
    const cardIng = screen.getByText('ING').closest('[role="button"]')!;
    fireEvent.click(cardIng);

    // Ahora abajo deben mostrarse los puestos de ING
    await waitFor(() => {
      expect(screen.getByText(/puestos de ing — ingeniería/i)).toBeInTheDocument();
      expect(screen.getByText('Ingeniero de Planta')).toBeInTheDocument();
    });
    // Y GER ya no debe aparecer en la sección inferior
    expect(screen.queryByText('Gerente General')).not.toBeInTheDocument();
  });

  it('abre modal con departamento destino fijado al pulsar Asignar puesto', async () => {
    let payloadRecibido: unknown = null;
    let departamentoDestinoId: string | null = null;

    mswServer.use(
      http.post(
        '*/api/v1/admin/empresas/sucursales/:sucursalId/puestos/:puestoId',
        async ({ request, params }) => {
          const body = (await request.json()) as { departamentoId: string };
          payloadRecibido = body;
          departamentoDestinoId = body.departamentoId;
          return HttpResponse.json(
            {
              sucursalId: SUCURSAL_ID,
              puestoId: params.puestoId,
              puestoClave: 'AUX',
              puestoNombre: 'Auxiliar General',
              departamentoId: body.departamentoId,
              estatus: 0,
              version: 1,
            },
            { status: 201 },
          );
        },
      ),
    );

    render(
      <SucursalOrganizacionTab
        sucursalId={SUCURSAL_ID}
        canGestionarDeptos={true}
        canGestionarPuestos={true}
      />,
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() => expect(screen.getByText('DIR')).toBeInTheDocument());

    // Pulsar botón de asignar puesto en la sección inferior
    const botonAsignarPuesto = screen.getByRole('button', {
      name: /^asignar puesto$/i,
    });
    fireEvent.click(botonAsignarPuesto);

    await waitFor(() =>
      expect(
        screen.getByRole('heading', {
          name: /asignar puesto al departamento/i,
        }),
      ).toBeInTheDocument(),
    );

    // El modal muestra que el destino es DIR
    expect(screen.getByText(/departamento destino:/i)).toBeInTheDocument();

    // Seleccionar el puesto AUX
    const selectPuesto = screen.getByLabelText(/puesto a asignar/i);
    fireEvent.change(selectPuesto, {
      target: { value: '00000000-0000-0000-0000-000000000003' },
    });

    // Confirmar en el modal
    const dialog = screen.getByRole('dialog');
    const botonConfirmar = within(dialog).getByRole('button', {
      name: /^asignar puesto$/i,
    });
    fireEvent.click(botonConfirmar);

    await waitFor(() => expect(payloadRecibido).not.toBeNull());
    // Se confirma que fue asignado al departamento actualmente activo (DIR)
    expect(departamentoDestinoId).toBe(DEPTO_DIR);
  });
});
