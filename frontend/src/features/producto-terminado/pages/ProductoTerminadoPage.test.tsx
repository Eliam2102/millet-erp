import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { ProductoTerminadoPage } from './ProductoTerminadoPage';
const mocks = vi.hoisted(() => ({
  list: vi.fn(),
  resolve: vi.fn(),
  mutate: vi.fn(),
  navigate: vi.fn(),
}));
vi.mock('@tanstack/react-router', () => ({
  useSearch: () => ({ page: 1 }),
  useNavigate: () => mocks.navigate,
}));
vi.mock('../api/hooks', () => ({ usePtList: mocks.list, useResolveScan: mocks.resolve }));
vi.mock('@/features/catalogos/api', () => ({
  useSucursales: () => ({ data: { items: [{ id: 'branch', nombre: 'Circuito' }] } }),
}));
vi.mock('@/components/erp/selectors/SucursalSelector', () => ({
  SucursalSelector: () => <button>Sucursal</button>,
}));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));
describe('Producto terminado', () => {
  beforeEach(() => {
    mocks.list.mockReturnValue({ data: { items: [], total: 0 }, refetch: vi.fn() });
    mocks.resolve.mockReturnValue({ mutate: mocks.mutate, reset: vi.fn(), isPending: false });
    mocks.mutate.mockClear();
  });
  it('muestra carga, vacío y error con acción de recuperación', () => {
    mocks.list.mockReturnValue({ isLoading: true });
    const view = render(<ProductoTerminadoPage />);
    expect(screen.getByLabelText('Cargando tabla')).toBeInTheDocument();
    mocks.list.mockReturnValue({ data: { items: [], total: 0 } });
    view.rerender(<ProductoTerminadoPage />);
    expect(screen.getByText('Sin registros')).toBeInTheDocument();
    mocks.list.mockReturnValue({ isError: true, error: new Error('offline'), refetch: vi.fn() });
    view.rerender(<ProductoTerminadoPage />);
    expect(screen.getAllByRole('alert').length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
  });
  it('presenta pedido, sucursal, hora y estado 69', () => {
    mocks.list.mockReturnValue({
      data: {
        total: 1,
        items: [
          {
            id: 'piece',
            pedido: '900101',
            posicion: 1,
            numeroPieza: 2,
            sucursalId: 'branch',
            escaneadaAt: '2026-10-30T12:00:00Z',
            estado: 'EnPT',
            estatusAw: 69,
          },
        ],
      },
    });
    render(<ProductoTerminadoPage />);
    expect(screen.getByText('900101')).toBeInTheDocument();
    expect(screen.getByText('Circuito')).toBeInTheDocument();
    expect(screen.getByText('En PT · 69')).toBeInTheDocument();
  });
  it('descartar exige motivo y entrega el escaneo y su versión a la mutación', () => {
    const scan = {
      id: 'scan',
      numeroPedido: '900101',
      posicion: 1,
      numeroPieza: 4,
      sucursalId: 'branch',
      resultado: 'Excepcion',
      motivo: 'PT_PIEZA_EXCEDE_POSICION',
      intentos: 2,
      version: 7,
    };
    mocks.list.mockReturnValue({ data: { total: 1, items: [scan] } });
    render(<ProductoTerminadoPage exceptions />);
    fireEvent.click(screen.getByRole('button', { name: 'Descartar' }));
    const submit = screen.getAllByRole('button', { name: 'Descartar' }).at(-1)!;
    expect(submit).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Motivo *'), {
      target: { value: 'Etiqueta incorrecta' },
    });
    fireEvent.click(submit);
    expect(mocks.mutate).toHaveBeenCalledWith(
      { scan, action: 'descartar', reason: 'Etiqueta incorrecta' },
      expect.any(Object),
    );
  });
});
