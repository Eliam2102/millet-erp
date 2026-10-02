import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { EmpleadoDetalle } from '@/modules/administracion/components/EmpleadoDetalle';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

vi.mock('@tanstack/react-router', () => ({
  useParams: () => ({ id: 'emp-1' }),
  Link: ({ children }: { children: React.ReactNode }) => <a>{children}</a>,
}));
vi.mock('@/modules/administracion/api/empleados', () => ({
  useEmpleadosAdmin: () => ({
    isLoading: false,
    isError: false,
    data: [{ id: 'emp-1', clave: 'EMP-001', nombre: 'Juana Pérez', email: null, estatus: 0, usuarioId: null }],
  }),
}));
vi.mock('@/features/catalogos/api', () => {
  const vacio = () => ({ data: { items: [] } });
  return { useSucursales: vacio, useDepartamentos: vacio, usePuestos: vacio };
});

const setPermisos = (permisos: string[]) =>
  useAuthStore.setState({ currentEmpresaId: 'e-1', permisos });

afterEach(() => setPermisos([]));

describe('<EmpleadoDetalle>', () => {
  it('oculta "Editar datos y transferir sucursal base" sin admin.empleados.gestionar', () => {
    setPermisos([]);
    render(<EmpleadoDetalle />, { wrapper: createQueryWrapper() });
    expect(screen.queryByRole('button', { name: /editar datos/i })).not.toBeInTheDocument();
  });

  it('lo muestra con admin.empleados.gestionar', () => {
    setPermisos([PermisosCanonicos.AdminEmpleadosGestionar]);
    render(<EmpleadoDetalle />, { wrapper: createQueryWrapper() });
    expect(screen.getByRole('button', { name: /editar datos/i })).toBeInTheDocument();
  });
});
