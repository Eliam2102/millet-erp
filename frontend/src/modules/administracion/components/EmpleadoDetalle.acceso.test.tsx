import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { EmpleadoDetalle } from '@/modules/administracion/components/EmpleadoDetalle';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';

/**
 * 86e3x6cpw · pestaña «Acceso» de la ficha: reúne lo que vivía en el detalle
 * de la cuenta (permisos personalizados, preferencias, centros de costo).
 * Los paneles internos se sustituyen por marcadores.
 */
const empleado = vi.hoisted(() => ({ usuarioId: 'u-9' as string | null }));

vi.mock('@tanstack/react-router', () => ({
  useParams: () => ({ id: 'emp-1' }),
  Link: ({ children }: { children: React.ReactNode }) => <a>{children}</a>,
}));
vi.mock('@/modules/administracion/api/empleados', () => ({
  useEmpleadosAdmin: () => ({
    isLoading: false,
    isError: false,
    data: [
      {
        id: 'emp-1',
        clave: 'EMP-001',
        nombre: 'Empleado Ficticio',
        email: 'ficticio@millet.mx',
        estatus: 0,
        usuarioId: empleado.usuarioId,
      },
    ],
  }),
}));
vi.mock('@/features/catalogos/api', () => {
  const vacio = () => ({ data: { items: [] } });
  return { useSucursales: vacio, useDepartamentos: vacio, usePuestos: vacio };
});
vi.mock('@/modules/identidad/api/usuarios', () => ({
  useUsuario: () => ({ isLoading: false, isError: false, data: { asignaciones: [] } }),
}));
vi.mock('../api/alcance-usuario', () => ({ useAlcanceUsuario: () => ({ data: undefined }) }));
vi.mock('./EmpleadoAccesoPanel', () => ({ EmpleadoAccesoPanel: () => <p>panel-acceso</p> }));
vi.mock('@/modules/identidad/components/RolesPorEmpresaPanel', () => ({
  RolesPorEmpresaPanel: () => <p>panel-roles</p>,
}));
vi.mock('@/modules/identidad/components/PermisosPersonalizadosPanel', () => ({
  PermisosPersonalizadosPanel: () => <p>panel-permisos-personalizados</p>,
}));
vi.mock('@/modules/identidad/components/PreferenciasPanel', () => ({
  PreferenciasPanel: ({ usuarioId }: { usuarioId: string }) => (
    <p>panel-preferencias:{usuarioId}</p>
  ),
}));
vi.mock('@/features/centros-costo/components/AsignacionUsuarioPanel', () => ({
  AsignacionUsuarioPanel: () => <p>panel-centros-costo</p>,
}));

function abrirAcceso(permisos: string[]) {
  useAuthStore.setState({ currentEmpresaId: 'e-1', permisos });
  render(<EmpleadoDetalle />, { wrapper: createQueryWrapper() });
  fireEvent.click(screen.getByRole('tab', { name: 'Acceso' }));
}

afterEach(() => {
  empleado.usuarioId = 'u-9';
  useAuthStore.setState({ permisos: [] });
});

describe('<EmpleadoDetalle> · pestaña Acceso (86e3x6cpw)', () => {
  it('muestra preferencias debajo de los permisos personalizados, sin enlace a la cuenta', () => {
    abrirAcceso([P.IdentidadUsuariosLeer]);
    const permisos = screen.getByText('panel-permisos-personalizados');
    const preferencias = screen.getByText('panel-preferencias:u-9');
    expect(
      permisos.compareDocumentPosition(preferencias) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    expect(screen.queryByText(/Abrir cuenta de acceso/)).not.toBeInTheDocument();
    expect(screen.queryByText('panel-centros-costo')).not.toBeInTheDocument();
  });

  it('sin permiso para leer la cuenta no muestra preferencias', () => {
    abrirAcceso([]);
    expect(screen.queryByText(/panel-preferencias/)).not.toBeInTheDocument();
  });

  it('sin cuenta (usuarioId nulo) no muestra preferencias', () => {
    empleado.usuarioId = null;
    abrirAcceso([P.IdentidadUsuariosLeer]);
    expect(screen.queryByText(/panel-preferencias/)).not.toBeInTheDocument();
  });

  it('con el permiso de asignaciones muestra los centros de costo', () => {
    abrirAcceso([P.IdentidadUsuariosLeer, P.CentrosCostoAsignacionesAdministrar]);
    expect(screen.getByText('panel-centros-costo')).toBeInTheDocument();
  });
});
