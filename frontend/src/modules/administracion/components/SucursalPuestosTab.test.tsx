import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SucursalPuestosTab } from '@/modules/administracion/components/SucursalPuestosTab';
import { useAuthStore } from '@/lib/auth/auth-store';

const SUCURSAL_ID = '00000000-0000-0000-0000-000000000010';

const CATALOGO_PUESTOS = {
  items: [
    {
      id: '00000000-0000-0000-0000-000000000001',
      clave: 'GER',
      nombre: 'Gerente General',
      estatus: 0,
      departamentoId: '00000000-0000-0000-0000-000000000099',
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

const DEPARTAMENTOS = {
  items: [
    {
      id: '00000000-0000-0000-0000-000000000099',
      clave: 'DIR',
      nombre: 'Dirección',
      estatus: 0,
    },
    {
      id: '00000000-0000-0000-0000-000000000088',
      clave: 'ADM',
      nombre: 'Administración',
      estatus: 0,
    },
  ],
  offset: 0,
  limit: 200,
  total: 2,
};

const ASIGNADOS_INICIALES = {
  items: [
    {
      sucursalId: SUCURSAL_ID,
      puestoId: '00000000-0000-0000-0000-000000000001',
      puestoClave: 'GER',
      puestoNombre: 'Gerente General',
      departamentoId: '00000000-0000-0000-0000-000000000099',
      departamentoNombre: 'Dirección',
      estatus: 0,
      version: 1,
    },
  ],
  total: 1,
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
    http.get('*/api/v1/catalogos/departamentos', () =>
      HttpResponse.json(DEPARTAMENTOS),
    ),
    http.get(
      '*/api/v1/admin/empresas/sucursales/:sucursalId/puestos',
      () => HttpResponse.json(ASIGNADOS_INICIALES),
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

describe('<SucursalPuestosTab>', () => {
  it('renderiza puestos asignados con su departamento y puestos sin asignar', async () => {
    render(<SucursalPuestosTab sucursalId={SUCURSAL_ID} canGestionar={true} />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => {
      expect(screen.getByText('GER')).toBeInTheDocument();
      expect(screen.getByText('AUX')).toBeInTheDocument();
    });

    expect(screen.getByText('Activa')).toBeInTheDocument();
    expect(screen.getByText('Sin asignar')).toBeInTheDocument();
    expect(screen.getByText('Depto sucursal:')).toBeInTheDocument();
  });

  it('abre modal al pulsar Asignar y envía departamentoId seleccionado', async () => {
    let payloadRecibido: unknown = null;

    mswServer.use(
      http.post(
        '*/api/v1/admin/empresas/sucursales/:sucursalId/puestos/:puestoId',
        async ({ request }) => {
          payloadRecibido = await request.json();
          return HttpResponse.json(
            {
              sucursalId: SUCURSAL_ID,
              puestoId: '00000000-0000-0000-0000-000000000002',
              puestoClave: 'AUX',
              puestoNombre: 'Auxiliar Administrativo',
              departamentoId: '00000000-0000-0000-0000-000000000088',
              departamentoNombre: 'Administración',
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

    await waitFor(() => expect(screen.getByText('AUX')).toBeInTheDocument());

    const botonAsignar = screen.getByRole('button', {
      name: /asignar puesto aux/i,
    });
    fireEvent.click(botonAsignar);

    await waitFor(() =>
      expect(
        screen.getByRole('heading', {
          name: /asignar puesto a departamento en sucursal/i,
        }),
      ).toBeInTheDocument(),
    );

    const select = screen.getByLabelText(/departamento asignado \*/i);
    fireEvent.change(select, {
      target: { value: '00000000-0000-0000-0000-000000000088' },
    });

    const botonConfirmar = screen.getByRole('button', {
      name: /^asignar puesto$/i,
    });
    fireEvent.click(botonConfirmar);

    await waitFor(() => expect(payloadRecibido).not.toBeNull());
    expect(payloadRecibido).toEqual({
      departamentoId: '00000000-0000-0000-0000-000000000088',
    });
  });
});
