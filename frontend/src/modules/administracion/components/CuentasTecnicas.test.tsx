import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { CuentasTecnicas } from '@/modules/administracion/components/CuentasTecnicas';
import { CuentaDetalle } from '@/modules/identidad/components/CuentaDetalle';

vi.mock('@tanstack/react-router', async (importOriginal) => {
  const original = await importOriginal<typeof import('@tanstack/react-router')>();
  return {
    ...original,
    Link: ({ children, params }: { children: React.ReactNode; params?: { id: string } }) => (
      <a href={`/admin/usuarios/${params?.id ?? ''}`}>{children}</a>
    ),
  };
});
vi.mock('@/modules/identidad/components/PermisosPersonalizadosPanel', () => ({
  PermisosPersonalizadosPanel: () => <div>permisos personalizados</div>,
}));

/**
 * 86e3yxe9m: cuentas técnicas visibles en Empleados y ficha de una cuenta sin
 * empleado con su rol y la conversión a cuenta de persona. Datos ficticios.
 */
const TECNICAS = {
  items: [
    {
      usuarioId: 'u-tec',
      nombre: 'Administrador Inicial',
      correo: 'admin.inicial@millet.mx',
      roles: ['Super administrador'],
      primerAccesoEn: '2026-10-01T10:00:00Z',
      activo: true,
      estadoAcceso: 0,
    },
  ],
  total: 1,
};

function detalle(id: string, tecnica: boolean, rolCodigo = 'super-admin') {
  return {
    usuario: {
      id,
      email: `${id}@millet.mx`,
      entraOid: `oid-${id}`,
      nombre: id === 'u-tec' ? 'Administrador Inicial' : 'Persona Actual',
      departamentoId: null,
      activo: true,
      version: 4,
      empleadoId: null,
      esCuentaTecnica: tecnica,
      primerAccesoEn: null,
    },
    asignaciones: [
      { id: `a-${id}`, empresaId: 'e-1', empresaRfc: 'MIL010101AB1', rolId: 'r-1', rolCodigo, fechaAsignacion: '2026-10-01T10:00:00Z' },
    ],
  };
}

let convertir: { body: unknown; version: string | null } | null = null;

function sesion(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-actual', email: 'actual@millet.mx', nombre: 'Persona Actual' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos,
    errorMessage: null,
  });
}

beforeEach(() => {
  convertir = null;
  sesion([P.AdminEmpleadosGestionar, P.IdentidadUsuariosLeer, P.IdentidadUsuariosEditar]);
  mswServer.use(
    http.get('*/api/v1/admin/colaboradores/cuentas-tecnicas', () => HttpResponse.json(TECNICAS)),
    http.get('*/api/v1/identidad/usuarios/u-tec', () => HttpResponse.json(detalle('u-tec', true))),
    http.get('*/api/v1/identidad/roles*', () => HttpResponse.json({ items: [], total: 0 })),
    http.post('*/api/v1/identidad/usuarios/u-tec/convertir-en-cuenta-de-persona', async ({ request }) => {
      convertir = { body: await request.json(), version: request.headers.get('X-Expected-Version') };
      return HttpResponse.json({ ...detalle('u-tec', false).usuario, version: 5 });
    }),
  );
});

afterEach(() => useAuthStore.setState({ permisos: [], accessToken: null, status: 'idle' }));

describe('Cuentas técnicas (86e3yxe9m)', () => {
  it('lista la cuenta técnica con su rol, primer acceso y estado, y el nombre lleva a su ficha', async () => {
    render(<CuentasTecnicas />, { wrapper: createQueryWrapper() });

    const enlace = await screen.findByRole('link', { name: 'Administrador Inicial' });
    expect(enlace).toHaveAttribute('href', '/admin/usuarios/u-tec');
    expect(screen.getByText('Super administrador')).toBeInTheDocument();
    expect(screen.getByText('admin.inicial@millet.mx')).toBeInTheDocument();
    expect(screen.getByText('Activa')).toBeInTheDocument();
  });
});

describe('Ficha de cuenta sin empleado (86e3yxe9m)', () => {
  it('muestra el rol de la cuenta y un super admin la convierte con motivo de 15 caracteres o más', async () => {
    mswServer.use(
      http.get('*/api/v1/identidad/usuarios/u-actual', () => HttpResponse.json(detalle('u-actual', false))),
    );
    render(<CuentaDetalle usuarioId="u-tec" />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Cuenta técnica')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Roles por empresa del usuario' })).toBeInTheDocument();
    fireEvent.click(await screen.findByRole('button', { name: /Convertir en cuenta de persona/ }));

    const confirmar = screen.getByRole('button', { name: 'Convertir' });
    fireEvent.change(screen.getByLabelText(/Motivo \*/), { target: { value: 'Muy corto' } });
    expect(confirmar).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/Motivo \*/), {
      target: { value: 'La cuenta es de una persona de sistemas' },
    });
    fireEvent.click(confirmar);

    await waitFor(() => expect(convertir).not.toBeNull());
    expect(convertir).toEqual({ body: { motivo: 'La cuenta es de una persona de sistemas' }, version: '4' });
  });

  it('quien no es super administrador no ve la acción de convertir', async () => {
    mswServer.use(
      http.get('*/api/v1/identidad/usuarios/u-actual', () =>
        HttpResponse.json(detalle('u-actual', false, 'contador')),
      ),
    );
    render(<CuentaDetalle usuarioId="u-tec" />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('Cuenta técnica')).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: /Convertir en cuenta de persona/ })).not.toBeInTheDocument(),
    );
  });
});
