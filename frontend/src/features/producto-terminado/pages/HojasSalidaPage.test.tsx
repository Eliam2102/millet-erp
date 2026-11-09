import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { HojasSalidaPage } from './HojasSalidaPage';

const state = vi.hoisted(() => ({ lista: vi.fn(), detalle: vi.fn(), accion: vi.fn(), crear: vi.fn() }));

vi.mock('@/components/erp', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/components/erp')>()),
  Breadcrumbs: () => null,
}));
vi.mock('@/components/erp/selectors/SucursalSelector', () => ({
  SucursalSelector: ({ placeholder, onChange }: { placeholder?: string; onChange: (id: string) => void }) => (
    <select aria-label={placeholder} onChange={(e) => onChange(e.target.value)}>
      <option value="">-</option>
      <option value="suc-1">Chichí Suárez</option>
    </select>
  ),
}));
vi.mock('@/features/facturacion/components/VehiculoPicker', () => ({ VehiculoPicker: () => <div>vehiculo</div> }));
vi.mock('@/features/facturacion/components/OperadorPicker', () => ({ OperadorPicker: () => <div>operador</div> }));
vi.mock('@/features/catalogos/api', () => ({
  useSucursales: () => ({ data: { items: [{ id: 'suc-1', nombre: 'Chichí Suárez' }] } }),
}));
vi.mock('../api/hooks', () => ({
  useHojasSalida: state.lista,
  useHojaSalida: state.detalle,
  useAccionHojaSalida: () => ({ mutateAsync: state.accion, isPending: false }),
  useCrearHojaSalida: () => ({ mutateAsync: state.crear, isPending: false }),
  useCargaHoja: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

const hoja = (estado: string) => ({
  id: 'h1', folio: 'HS-20261105-A1B2C', sucursalId: 'suc-1', destino: 'Obra Mérida norte', foranea: false, vehiculoId: null, operadorId: null, estado,
  creadaAt: '2026-11-05T12:00:00Z', emitidaAt: null, cartaPorteFolio: null, pedidos: 1, piezas: 20, albaranes: [], yaEmitida: false,
});
const detalle = {
  ...hoja('Emitida'),
  albaranes: [{
    id: 'a1', folio: 'HS-20261105-A1B2C/1', secuencia: 1, numeroPedido: '260001', cliente: 'Constructora Peninsular', destino: 'Torre Altabrisa', emitidoAt: '2026-11-05T12:05:00Z', total: 20,
    lineas: [{ posicion: 1, descripcion: 'Templado 6 mm', piezas: 20 }], etiquetas: ['E1', 'E2'],
  }],
};

beforeEach(() => {
  vi.clearAllMocks();
  state.accion.mockResolvedValue({ yaEmitida: false });
  state.crear.mockResolvedValue({ id: 'h1' });
  state.detalle.mockReturnValue({ data: undefined });
  useAuthStore.setState({ permisos: [P.ProductoTerminadoPiezasVer, P.ProductoTerminadoExcepcionesGestionar] });
});

describe('HojasSalidaPage', () => {
  it('una hoja en carga se emite o se cancela; una emitida solo se imprime o se reintenta sin efecto', () => {
    state.lista.mockReturnValue({ data: { items: [hoja('Borrador')], total: 1 }, isLoading: false, isError: false, refetch: vi.fn() });
    render(<HojasSalidaPage />);
    expect(screen.getByText('En carga')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Emitir' }));
    expect(state.accion).toHaveBeenCalledWith({ id: 'h1', accion: 'emitir', estado: 'Borrador' });
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
    expect(state.accion).toHaveBeenCalledWith({ id: 'h1', accion: 'cancelar', estado: 'Borrador' });
  });

  // M15: una hoja emitida no se vuelve a emitir (no tendría efecto); se ven y reimprimen sus albaranes.
  it('una hoja emitida ofrece ver o reimprimir los albaranes, no emitir de nuevo', () => {
    state.lista.mockReturnValue({ data: { items: [hoja('Emitida')], total: 1 }, isLoading: false, isError: false, refetch: vi.fn() });
    state.detalle.mockReturnValue({ data: detalle });
    render(<HojasSalidaPage />);
    expect(screen.queryByRole('button', { name: 'Cancelar' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Emitir de nuevo' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Emitir' })).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Ver o reimprimir albaranes' }));
    expect(screen.getByRole('button', { name: 'Ocultar' })).toBeInTheDocument();
    expect(state.accion).not.toHaveBeenCalled();
  });

  it('el detalle muestra la hoja con pedidos, clientes y secuencia, e imprime', () => {
    state.lista.mockReturnValue({ data: { items: [hoja('Emitida')], total: 1 }, isLoading: false, isError: false, refetch: vi.fn() });
    state.detalle.mockReturnValue({ data: detalle });
    const imprimir = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    render(<HojasSalidaPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Ver o reimprimir albaranes' }));
    expect(screen.getByText('Constructora Peninsular')).toBeInTheDocument();
    expect(screen.getByText('260001')).toBeInTheDocument();
    expect(screen.getByText(/20 pzas · partida 1 · Templado 6 mm/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Imprimir hoja de salida/ }));
    expect(imprimir).toHaveBeenCalled();
  });

  it('sin obra en el pedido, el albarán muestra el destino de la hoja (M14)', () => {
    state.lista.mockReturnValue({ data: { items: [hoja('Emitida')], total: 1 }, isLoading: false, isError: false, refetch: vi.fn() });
    state.detalle.mockReturnValue({ data: { ...detalle, albaranes: [{ ...detalle.albaranes[0], destino: null }] } });
    render(<HojasSalidaPage />);
    // La hoja emitida ofrece «Ver o reimprimir albaranes» (7121b7e9d).
    fireEvent.click(screen.getByRole('button', { name: 'Ver o reimprimir albaranes' }));
    const nota = screen.getByText('Destino de la hoja: el pedido no trae obra');
    expect(nota.closest('td')).toHaveTextContent('Obra Mérida norte');
    expect(screen.queryByText('Sin obra')).toBeNull();
  });

  it('arma la hoja con las etiquetas cargadas por pedido; la foránea pide los datos de la Carta Porte', async () => {
    state.lista.mockReturnValue({ data: { items: [], total: 0 }, isLoading: false, isError: false, refetch: vi.fn() });
    render(<HojasSalidaPage />);
    const armar = screen.getByRole('button', { name: 'Armar hoja de salida' });
    expect(armar).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Sucursal de salida'), { target: { value: 'suc-1' } });
    fireEvent.change(screen.getByLabelText('Destino de la ruta'), { target: { value: 'Obra Mérida norte' } });
    fireEvent.change(screen.getByLabelText('Pedido de la entrega 1'), { target: { value: '260001' } });
    fireEvent.change(screen.getByLabelText('Etiquetas cargadas del pedido 1'), { target: { value: 'E1\nE2\n' } });
    expect(armar).toBeEnabled();
    fireEvent.click(screen.getByLabelText('Entrega foránea o por carretera federal'));
    expect(armar).toBeDisabled();
    fireEvent.click(screen.getByLabelText('Entrega foránea o por carretera federal'));
    fireEvent.click(armar);
    await waitFor(() => expect(state.crear).toHaveBeenCalled());
    expect(state.crear.mock.calls[0]![0]).toMatchObject({
      sucursalId: 'suc-1', destino: 'Obra Mérida norte', foranea: false, pedidos: [{ numeroPedido: '260001', etiquetas: ['E1', 'E2'] }],
    });
  });
});
