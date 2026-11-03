import type {} from '@/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { identidadAdminCards } from '@/modules/identidad/admin';
import { Route as listado } from '@/routes/_app/admin/usuarios/index';
import { Route as detalle } from '@/routes/_app/admin/usuarios/$id';

/**
 * 86e3x6cpw · F2: «Cuentas de acceso» desaparece; sus rutas viejas llevan a
 * Empleados (contrato 2 para el detalle).
 */
type Guard = (ctx: { params: { id: string } }) => Promise<void> | void;

async function destino(guard: Guard, id = 'u-1') {
  try {
    await guard({ params: { id } });
  } catch (r) {
    return (r as { options: unknown }).options;
  }
  throw new Error('Debía redirigir');
}

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-admin', email: 'admin@millet.mx', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [P.AdminEmpleadosGestionar],
    errorMessage: null,
  });
});

afterEach(() => useAuthStore.setState({ permisos: [], accessToken: null, status: 'idle' }));

describe('rutas viejas de «Cuentas de acceso» (86e3x6cpw)', () => {
  it('/admin/usuarios lleva a Empleados, vista «Cuentas sin empleado»', async () => {
    expect(await destino(listado.options.beforeLoad as Guard)).toMatchObject({
      to: '/admin/empleados',
      search: { vista: 'sin-empleado' },
    });
  });

  it('/admin/usuarios/$id lleva a la ficha del empleado ligado a la cuenta', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/colaboradores/por-usuario/u-1', () =>
        HttpResponse.json({ empleadoId: 'emp-7' }),
      ),
    );
    expect(await destino(detalle.options.beforeLoad as Guard)).toMatchObject({
      to: '/admin/empleados/$id',
      params: { id: 'emp-7' },
    });
  });

  it('/admin/usuarios/$id sin empleado (404) lleva a «Cuentas sin empleado»', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/colaboradores/por-usuario/u-1', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Empleado no encontrado',
            status: 404,
            code: 'EMPLEADO_NO_ENCONTRADO',
          },
          { status: 404 },
        ),
      ),
    );
    expect(await destino(detalle.options.beforeLoad as Guard)).toMatchObject({
      to: '/admin/empleados',
      search: { vista: 'sin-empleado' },
    });
  });

  it('/admin/usuarios/$id sin permiso de Empleados lleva a inicio', async () => {
    useAuthStore.setState({ permisos: [P.IdentidadUsuariosLeer] });
    expect(await destino(detalle.options.beforeLoad as Guard)).toMatchObject({ to: '/' });
  });

  it('el menú de Administración ya no ofrece «Cuentas de acceso»; «Roles y permisos» se queda', () => {
    expect(identidadAdminCards.map((c) => c.titulo)).toEqual(['Roles y permisos']);
    expect(identidadAdminCards.some((c) => c.href === '/admin/usuarios')).toBe(false);
  });
});
