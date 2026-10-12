import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { AlcanceUsuarioPanel } from './AlcanceUsuarioPanel';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
const mocks = vi.hoisted(() => ({ mutate: vi.fn(), query: { data: { esCorporativo: true, sucursales: [] as string[] }, isPending: false, isError: false } }));
vi.mock('../api/alcance-usuario', () => ({ useAlcanceUsuario: () => mocks.query, useActualizarAlcanceUsuario: () => ({ mutate: mocks.mutate, isPending: false }) }));
vi.mock('@/features/catalogos/api', () => ({ useSucursales: () => ({ data: { items: [{ id: 'mty', clave: 'MTY', nombre: 'Monterrey', estatus: 0 }] } }) }));
beforeEach(() => { mocks.query.data = { esCorporativo: true, sucursales: [] }; useAuthStore.setState({ permisos: [PermisosCanonicos.AdminSucursalesUsuariosGestionar, PermisosCanonicos.AdminEmpleadosGestionarTodasSucursales] }); });
afterEach(() => { vi.clearAllMocks(); useAuthStore.setState({ permisos: [] }); });
describe('Alcance corporativo del empleado', () => {
  it('muestra corporativo sin asignaciones individuales y permite transferir a MTY', () => {
    render(<AlcanceUsuarioPanel usuarioId="u-1" />);
    expect(screen.getByText('Corporativo · todas las sucursales')).toBeInTheDocument();
    expect(screen.queryByLabelText('MTY · Monterrey')).not.toBeInTheDocument();
    fireEvent.click(screen.getByLabelText('Usuario corporativo de Millet'));
    fireEvent.click(screen.getByLabelText('MTY · Monterrey'));
    fireEvent.click(screen.getByRole('button', { name: 'Guardar alcance' }));
    expect(mocks.mutate).toHaveBeenCalledWith({ esCorporativo: false, sucursales: ['mty'] }, expect.any(Object));
  });
  it('no permite ampliar alcance sin permiso de administrar toda la empresa', () => {
    useAuthStore.setState({ permisos: [PermisosCanonicos.AdminSucursalesUsuariosGestionar] });
    render(<AlcanceUsuarioPanel usuarioId="u-1" />);
    expect(screen.getByLabelText('Usuario corporativo de Millet')).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Guardar alcance' })).not.toBeInTheDocument();
  });
});
