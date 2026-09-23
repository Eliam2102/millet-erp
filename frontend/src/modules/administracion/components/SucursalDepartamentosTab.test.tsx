import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SucursalDepartamentosTab } from '@/modules/administracion/components/SucursalDepartamentosTab';
import { useAuthStore } from '@/lib/auth/auth-store';

const SUCURSAL_ID = '00000000-0000-0000-0000-000000000010';

const CATALOGO_DEPARTAMENTOS = {
  items: [
    {
      id: '00000000-0000-0000-0000-000000000099',
      clave: 'DIR',
      nombre: 'Dirección General',
      estatus: 0,
      version: 1,
    },
    {
      id: '00000000-0000-0000-0000-000000000088',
      clave: 'ING',
      nombre: 'Ingeniería',
      estatus: 0,
      version: 1,
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
      departamentoId: '00000000-0000-0000-0000-000000000099',
      departamentoClave: 'DIR',
      departamentoNombre: 'Dirección General',
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
    permisos: ['admin.sucursales.departamentos-gestionar'],
    errorMessage: null,
  });

  mswServer.use(
    http.get('*/api/v1/catalogos/departamentos', () =>
      HttpResponse.json(CATALOGO_DEPARTAMENTOS),
    ),
    http.get(
      '*/api/v1/admin/empresas/sucursales/:sucursalId/departamentos',
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

describe('<SucursalDepartamentosTab>', () => {
  it('renderiza solo departamentos asignados y no los no asignados en la lista principal', async () => {
    render(<SucursalDepartamentosTab sucursalId={SUCURSAL_ID} canGestionar={true} />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => {
      expect(screen.getByText('DIR')).toBeInTheDocument();
      expect(screen.getByText('Dirección General')).toBeInTheDocument();
    });

    expect(screen.getByText('Activo')).toBeInTheDocument();

    // El departamento no asignado (ING) no debe aparecer en la lista inicial
    expect(screen.queryByText('ING')).not.toBeInTheDocument();
    expect(screen.queryByText('Ingeniería')).not.toBeInTheDocument();
  });

  it('abre modal al pulsar Asignar departamento y envía departamentoId con Idempotency-Key fresca', async () => {
    let payloadDepartamentoId: string | null = null;
    let idempotencyKeyRecibida: string | null = null;

    mswServer.use(
      http.post(
        '*/api/v1/admin/empresas/sucursales/:sucursalId/departamentos/:departamentoId',
        async ({ request, params }) => {
          payloadDepartamentoId = params.departamentoId as string;
          idempotencyKeyRecibida = request.headers.get('Idempotency-Key');
          return HttpResponse.json(
            {
              sucursalId: SUCURSAL_ID,
              departamentoId: params.departamentoId,
              departamentoClave: 'ING',
              departamentoNombre: 'Ingeniería',
              estatus: 0,
              version: 1,
            },
            { status: 201 },
          );
        },
      ),
    );

    render(<SucursalDepartamentosTab sucursalId={SUCURSAL_ID} canGestionar={true} />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(screen.getByText('DIR')).toBeInTheDocument());

    const botonAbrir = screen.getByRole('button', {
      name: /^asignar departamento$/i,
    });
    fireEvent.click(botonAbrir);

    await waitFor(() =>
      expect(
        screen.getByRole('heading', {
          name: /asignar departamento a la sucursal/i,
        }),
      ).toBeInTheDocument(),
    );

    // Seleccionar ING en el dropdown
    const select = screen.getByLabelText(/departamento a asignar/i);
    fireEvent.change(select, {
      target: { value: '00000000-0000-0000-0000-000000000088' },
    });

    // Confirmar en el modal
    const dialog = screen.getByRole('dialog');
    const botonConfirmar = within(dialog).getByRole('button', {
      name: /asignar a sucursal/i,
    });
    fireEvent.click(botonConfirmar);

    await waitFor(() => expect(payloadDepartamentoId).not.toBeNull());
    expect(payloadDepartamentoId).toBe('00000000-0000-0000-0000-000000000088');
    expect(idempotencyKeyRecibida).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i,
    );
  });
});
