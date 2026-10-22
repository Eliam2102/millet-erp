import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { ActivosPage } from './ActivosPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { estadoActivo, dinero } from '../api/types';
const state = vi.hoisted(() => ({
  navigate: vi.fn(),
  abrir: vi.fn(),
  lista: {
    data: { items: [] as unknown[], total: 0 },
    isPending: false,
    error: null as Error | null,
  },
}));
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children }: { children: React.ReactNode }) => <a>{children}</a>,
  useNavigate: () => state.navigate,
  useSearch: () => ({}),
}));
vi.mock('../api/hooks', () => ({
  useActivos: () => state.lista,
  useClasesActivo: () => ({ data: [] }),
}));
vi.mock('../components/nuevo-activo-context', () => ({
  useNuevoActivo: () => ({ abrir: state.abrir }),
}));
vi.mock('@/components/erp/selectors/SucursalSelector', () => ({ SucursalSelector: () => null }));
vi.mock('@/components/erp/selectors/UsuarioSelector', () => ({ UsuarioSelector: () => null }));
vi.mock('@/features/centros-costo/components/Dim3Picker', () => ({ Dim3Picker: () => null }));
describe('Activos fijos', () => {
  beforeEach(() => {
    state.lista = { data: { items: [], total: 0 }, isPending: false, error: null };
    state.abrir.mockClear();
    state.navigate.mockClear();
    useAuthStore.setState({ permisos: [P.ActivosFijosActivosVer] });
  });
  it('muestra vacío y oculta alta sin permiso', () => {
    render(<ActivosPage />);
    expect(screen.getByText('No hay activos con estos filtros.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Alta manual' })).not.toBeInTheDocument();
  });
  it('abre alta y permite filtrar por capitalizar usando cero', () => {
    useAuthStore.setState({ permisos: [P.ActivosFijosActivosVer, P.ActivosFijosActivosGestionar] });
    render(<ActivosPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Alta manual' }));
    expect(state.abrir).toHaveBeenCalledOnce();
    fireEvent.change(screen.getByLabelText('Estado'), { target: { value: '0' } });
    expect(state.navigate).toHaveBeenCalledWith({
      to: '/activos-fijos/activos',
      search: { offset: 0, estado: 0 },
    });
  });
  it('expone error sin inventar filas', () => {
    state.lista.error = new Error('No fue posible consultar activos');
    render(<ActivosPage />);
    expect(screen.getByRole('alert')).toHaveTextContent('No fue posible consultar activos');
  });
  it('conserva importes y estados del dominio', () => {
    expect(dinero(480000)).toContain('480,000.00');
    expect(estadoActivo[0]).toBe('Por capitalizar');
    expect(estadoActivo[1]).toBe('Activo');
  });
});
