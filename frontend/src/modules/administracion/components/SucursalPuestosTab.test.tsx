import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
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

const DEPARTAMENTOS_DE_SUCURSAL = {
  items: [
    {
      sucursalId: SUCURSAL_ID,
      departamentoId: '00000000-0000-0000-0000-000000000099',
      departamentoClave: 'DIR',
      departamentoNombre: 'Dirección',
      estatus: 0,
      version: 1,
    },
    {
      sucursalId: SUCURSAL_ID,
      departamentoId: '00000000-0000-0000-0000-000000000088',
      departamentoClave: 'ADM',
      departamentoNombre: 'Administración',
      estatus: 0,
      version: 1,
    },
  ],
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
    http.get(
      '*/api/v1/admin/empresas/sucursales/:sucursalId/departamentos',
      () => HttpResponse.json(DEPARTAMENTOS_DE_SUCURSAL),
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
  it('renderiza únicamente los puestos asignados a la sucursal y no los no asignados', async () => {
    render(<SucursalPuestosTab sucursalId={SUCURSAL_ID} canGestionar={true} />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => {
      expect(screen.getByText('GER')).toBeInTheDocument();
      expect(screen.getByText('Gerente General')).toBeInTheDocument();
    });

    expect(screen.getByText('Activa')).toBeInTheDocument();
    expect(screen.getByText('Depto sucursal:')).toBeInTheDocument();
    expect(screen.getByText('Dirección')).toBeInTheDocument();

    // El puesto sin asignar no debe aparecer en la lista principal
    expect(screen.queryByText('Auxiliar Administrativo')).not.toBeInTheDocument();
    expect(screen.queryByText('Sin asignar')).not.toBeInTheDocument();
  });

  it('abre modal al pulsar Asignar puesto y envía puestoId y departamentoId con Idempotency-Key fresca', async () => {
    let payloadRecibido: unknown = null;
    let idempotencyKeyRecibida: string | null = null;
    let puestoIdEnviado: string | null = null;

    mswServer.use(
      http.post(
        '*/api/v1/admin/empresas/sucursales/:sucursalId/puestos/:puestoId',
        async ({ request, params }) => {
          puestoIdEnviado = params.puestoId as string;
          idempotencyKeyRecibida = request.headers.get('Idempotency-Key');
          payloadRecibido = await request.json();
          return HttpResponse.json(
            {
              sucursalId: SUCURSAL_ID,
              puestoId: params.puestoId,
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

    await waitFor(() => expect(screen.getByText('GER')).toBeInTheDocument());

    const botonAbrirModal = screen.getByRole('button', {
      name: /^asignar puesto$/i,
    });
    fireEvent.click(botonAbrirModal);

    await waitFor(() =>
      expect(
        screen.getByRole('heading', {
          name: /asignar puesto a departamento en sucursal/i,
        }),
      ).toBeInTheDocument(),
    );

    // Seleccionar puesto en el modal
    const selectPuesto = screen.getByLabelText(/puesto a asignar/i);
    fireEvent.change(selectPuesto, {
      target: { value: '00000000-0000-0000-0000-000000000002' },
    });

    // Seleccionar departamento en el modal
    const selectDepto = screen.getByLabelText(/departamento asignado \*/i);
    fireEvent.change(selectDepto, {
      target: { value: '00000000-0000-0000-0000-000000000088' },
    });

    // Confirmar en el modal
    const dialog = screen.getByRole('dialog');
    const botonConfirmar = within(dialog).getByRole('button', {
      name: /^asignar puesto$/i,
    });
    fireEvent.click(botonConfirmar);

    await waitFor(() => expect(payloadRecibido).not.toBeNull());
    expect(puestoIdEnviado).toBe('00000000-0000-0000-0000-000000000002');
    expect(payloadRecibido).toEqual({
      departamentoId: '00000000-0000-0000-0000-000000000088',
    });
    expect(idempotencyKeyRecibida).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i,
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
});
