import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { TraspasosPage } from './TraspasosPage';

const state = vi.hoisted(() => ({ lista: vi.fn(), detalle: vi.fn(), recibir: vi.fn(), crear: vi.fn() }));

vi.mock('@/components/erp', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/components/erp')>()),
  Breadcrumbs: () => null,
}));
vi.mock('@/components/erp/selectors/SucursalSelector', () => ({
  SucursalSelector: ({ placeholder, onChange }: { placeholder?: string; onChange: (id: string) => void }) => (
    <select aria-label={placeholder} onChange={(e) => onChange(e.target.value)}>
      <option value="">-</option>
      <option value="suc-1">Conkal</option>
      <option value="suc-2">Cancún</option>
    </select>
  ),
}));
vi.mock('@/features/catalogos/api', () => ({
  useSucursales: () => ({ data: { items: [{ id: 'suc-1', nombre: 'Conkal' }, { id: 'suc-2', nombre: 'Cancún' }] } }),
}));
vi.mock('../api/hooks', () => ({
  useTraspasos: state.lista,
  useTraspaso: state.detalle,
  useRecibirTraspaso: () => ({ mutateAsync: state.recibir, isPending: false }),
  useCrearTraspaso: () => ({ mutateAsync: state.crear, isPending: false }),
}));

const traspaso = (estado: string, pendientes: number) => ({
  id: 't1', sucursalOrigenId: 'suc-1', sucursalDestinoId: 'suc-2', estado, salidaAt: '2026-11-04T12:00:00Z', recibidoAt: null, total: 2, pendientes, piezas: [],
});
const detalle = {
  ...traspaso('ConDiferencia', 1),
  piezas: [
    { piezaId: 'p1', etiqueta: 'E1', numeroPedido: 'P-1', recibida: true },
    { piezaId: 'p2', etiqueta: 'E2', numeroPedido: 'P-1', recibida: false },
  ],
};

beforeEach(() => {
  vi.clearAllMocks();
  state.recibir.mockResolvedValue({});
  state.crear.mockResolvedValue({});
  state.detalle.mockReturnValue({ data: undefined });
  useAuthStore.setState({ permisos: [P.ProductoTerminadoPiezasVer, P.ProductoTerminadoExcepcionesGestionar] });
});

describe('TraspasosPage', () => {
  it('muestra el traspaso en tránsito con sus pendientes y recibe todo', () => {
    state.lista.mockReturnValue({ data: { items: [traspaso('EnTransito', 2)], total: 1 }, isLoading: false, isError: false, refetch: vi.fn() });
    render(<TraspasosPage />);
    expect(screen.getByText('En tránsito')).toBeInTheDocument();
    expect(screen.getByText('2 de 2')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Recibir todo' }));
    expect(state.recibir).toHaveBeenCalledWith({ id: 't1', piezaIds: undefined });
  });

  it('recepción parcial: solo manda las piezas marcadas', () => {
    state.lista.mockReturnValue({ data: { items: [traspaso('ConDiferencia', 1)], total: 1 }, isLoading: false, isError: false, refetch: vi.fn() });
    state.detalle.mockReturnValue({ data: detalle });
    render(<TraspasosPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Ver piezas' }));
    expect(screen.getByText('Con diferencia')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('checkbox', { name: 'Recibir E2' }));
    fireEvent.click(screen.getByRole('button', { name: 'Recibir seleccionadas' }));
    expect(state.recibir).toHaveBeenCalledWith({ id: 't1', piezaIds: ['p2'] });
  });

  it('registra la salida con las etiquetas de cada renglón', () => {
    state.lista.mockReturnValue({ data: { items: [], total: 0 }, isLoading: false, isError: false, refetch: vi.fn() });
    render(<TraspasosPage />);
    fireEvent.change(screen.getByLabelText('Sucursal origen'), { target: { value: 'suc-1' } });
    fireEvent.change(screen.getByLabelText('Sucursal destino'), { target: { value: 'suc-2' } });
    fireEvent.change(screen.getByLabelText('Etiquetas de las piezas'), { target: { value: 'E1\n E2 \n' } });
    fireEvent.click(screen.getByRole('button', { name: 'Registrar salida' }));
    expect(state.crear).toHaveBeenCalledWith({ sucursalOrigenId: 'suc-1', sucursalDestinoId: 'suc-2', etiquetas: ['E1', 'E2'] });
  });

  it('solo con piezas.ver no ofrece operar', () => {
    useAuthStore.setState({ permisos: [P.ProductoTerminadoPiezasVer] });
    state.lista.mockReturnValue({ data: { items: [traspaso('EnTransito', 2)], total: 1 }, isLoading: false, isError: false, refetch: vi.fn() });
    render(<TraspasosPage />);
    expect(screen.queryByRole('button', { name: 'Recibir todo' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Registrar salida' })).toBeNull();
  });
});
