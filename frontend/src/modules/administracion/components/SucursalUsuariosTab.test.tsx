import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SucursalUsuariosTab } from '@/modules/administracion/components/SucursalUsuariosTab';
import { useAuthStore } from '@/lib/auth/auth-store';

const SUCURSAL_ID = '00000000-0000-0000-0000-000000000010';

const CATALOGO_USUARIOS = {
  items: [
    {
      id: '00000000-0000-0000-0000-000000000111',
      email: 'juan@millet.com',
      nombre: 'Juan Pérez',
      activo: true,
      roles: [],
    },
    {
      id: '00000000-0000-0000-0000-000000000222',
      email: 'maria@millet.com',
      nombre: 'María López',
      activo: true,
      roles: [],
    },
  ],
  total: 2,
};

const ASIGNADOS_INICIALES = {
  items: [
    {
      sucursalId: SUCURSAL_ID,
      usuarioId: '00000000-0000-0000-0000-000000000111',
      usuarioEmail: 'juan@millet.com',
      usuarioNombre: 'Juan Pérez',
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
    permisos: ['admin.sucursales.usuarios-gestionar'],
    errorMessage: null,
  });

  mswServer.use(
    http.get('*/api/v1/identidad/usuarios/admin', () =>
      HttpResponse.json(CATALOGO_USUARIOS),
    ),
    http.get(
      '*/api/v1/admin/empresas/sucursales/:sucursalId/usuarios',
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

describe('<SucursalUsuariosTab>', () => {
  it('renderiza solo usuarios asignados y no los no asignados en la lista principal', async () => {
    render(<SucursalUsuariosTab sucursalId={SUCURSAL_ID} canGestionar={true} />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => {
      expect(screen.getByText('Juan Pérez')).toBeInTheDocument();
      expect(screen.getByText('juan@millet.com')).toBeInTheDocument();
    });

    expect(screen.getByText('Activo')).toBeInTheDocument();

    // El usuario no asignado (María) no debe aparecer en la lista inicial
    expect(screen.queryByText('María López')).not.toBeInTheDocument();
    expect(screen.queryByText('maria@millet.com')).not.toBeInTheDocument();
  });

  it('abre modal al pulsar Asignar usuario y envía usuarioId con Idempotency-Key fresca', async () => {
    let payloadUsuarioId: string | null = null;
    let idempotencyKeyRecibida: string | null = null;

    mswServer.use(
      http.post(
        '*/api/v1/admin/empresas/sucursales/:sucursalId/usuarios/:usuarioId',
        async ({ request, params }) => {
          payloadUsuarioId = params.usuarioId as string;
          idempotencyKeyRecibida = request.headers.get('Idempotency-Key');
          return HttpResponse.json(
            {
              sucursalId: SUCURSAL_ID,
              usuarioId: params.usuarioId,
              usuarioEmail: 'maria@millet.com',
              usuarioNombre: 'María López',
              estatus: 0,
              version: 1,
            },
            { status: 201 },
          );
        },
      ),
    );

    render(<SucursalUsuariosTab sucursalId={SUCURSAL_ID} canGestionar={true} />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(screen.getByText('Juan Pérez')).toBeInTheDocument());

    const botonAbrir = screen.getByRole('button', {
      name: /^asignar usuario$/i,
    });
    fireEvent.click(botonAbrir);

    await waitFor(() =>
      expect(
        screen.getByRole('heading', {
          name: /asignar usuario a la sucursal/i,
        }),
      ).toBeInTheDocument(),
    );

    // Seleccionar María en el dropdown
    const select = screen.getByLabelText(/usuario a asignar/i);
    fireEvent.change(select, {
      target: { value: '00000000-0000-0000-0000-000000000222' },
    });

    // Confirmar en el modal
    const dialog = screen.getByRole('dialog');
    const botonConfirmar = within(dialog).getByRole('button', {
      name: /asignar usuario/i,
    });
    fireEvent.click(botonConfirmar);

    await waitFor(() => expect(payloadUsuarioId).not.toBeNull());
    expect(payloadUsuarioId).toBe('00000000-0000-0000-0000-000000000222');
    expect(idempotencyKeyRecibida).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i,
    );
  });
});
