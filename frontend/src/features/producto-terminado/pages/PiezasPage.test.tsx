import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { PiezasPage } from './PiezasPage';

const state = vi.hoisted(() => ({ usePiezas: vi.fn(), refetch: vi.fn() }));

vi.mock('@/components/erp', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/components/erp')>()),
  Breadcrumbs: () => null,
}));
vi.mock('@/components/erp/selectors/SucursalSelector', () => ({
  SucursalSelector: ({ value, onChange }: { value?: string | null; onChange: (id: string) => void }) => (
    <select aria-label="Sucursal" value={value ?? ''} onChange={(e) => onChange(e.target.value)}>
      <option value="">Todas</option>
      <option value="suc-1">Conkal</option>
    </select>
  ),
}));
vi.mock('@/features/catalogos/api', () => ({
  useSucursales: () => ({ data: { items: [{ id: 'suc-1', nombre: 'Conkal' }] } }),
}));
vi.mock('../api/hooks', () => ({ usePiezas: state.usePiezas }));

const ok = (data: unknown) => ({ data, isLoading: false, isError: false, refetch: state.refetch });

beforeEach(() => vi.clearAllMocks());

describe('PiezasPage', () => {
  it('muestra estado vacío', () => {
    state.usePiezas.mockReturnValue(ok({ items: [], total: 0, avance: [] }));
    render(<PiezasPage />);
    expect(screen.getByText('No hay piezas para esos filtros')).toBeInTheDocument();
  });

  it('muestra filas y avance, filtra con pedido y sucursal, sin rastro de 69', () => {
    state.usePiezas.mockReturnValue(
      ok({
        items: [
          { id: 'p1', etiqueta: 'ETQ-001', numeroPedido: 'PED-1', posicion: 2, numeroPieza: 3, sucursalId: 'suc-1', estado: 'EnPt', escaneadaAt: '2026-10-20T18:00:00Z' },
        ],
        total: 1,
        avance: [{ posicion: 2, descripcion: null, esperadas: 5, terminadas: 3, enPt: 3, cargadas: 0, entregadas: 0, pendientes: 2, avanceFisico: 'Parcial' }],
      }),
    );
    const { container } = render(<PiezasPage />);
    fireEvent.change(screen.getByLabelText('Pedido'), { target: { value: 'PED-1' } });
    fireEvent.change(screen.getByLabelText('Sucursal'), { target: { value: 'suc-1' } });
    expect(state.usePiezas).toHaveBeenLastCalledWith(expect.objectContaining({ pedido: 'PED-1', sucursalId: 'suc-1' }));
    expect(screen.getByText('ETQ-001')).toBeInTheDocument();
    expect(screen.getAllByText('Conkal').length).toBeGreaterThan(0);
    expect(screen.getAllByText('En PT').length).toBeGreaterThan(0);
    expect(screen.getByText('Parcial')).toBeInTheDocument();
    for (const k of ['Esperadas', 'Terminadas', 'Cargadas', 'Entregadas', 'Pendientes']) expect(screen.getByText(k)).toBeInTheDocument();
    expect(container.textContent).not.toContain('69');
  });

  it('una pieza entregada o rechazada en sitio (PT5.6) no se muestra como En PT', () => {
    state.usePiezas.mockReturnValue(
      ok({
        items: [
          { id: 'p1', etiqueta: 'ETQ-001', numeroPedido: 'PED-1', posicion: 1, numeroPieza: 1, sucursalId: 'suc-1', estado: 'Entregada', escaneadaAt: '2026-10-20T18:00:00Z' },
          { id: 'p2', etiqueta: 'ETQ-002', numeroPedido: 'PED-1', posicion: 1, numeroPieza: 2, sucursalId: 'suc-1', estado: 'EnRevision', escaneadaAt: '2026-10-20T18:00:00Z' },
        ],
        total: 2,
        avance: [],
      }),
    );
    render(<PiezasPage />);
    expect(screen.getByText('Entregada')).toBeInTheDocument();
    expect(screen.getByText('En revisión')).toBeInTheDocument();
    expect(screen.queryByText('En PT')).not.toBeInTheDocument();
  });

  it('una pieza en tránsito muestra En tránsito con su destino, no En PT', () => {
    state.usePiezas.mockReturnValue(
      ok({
        items: [
          { id: 'p1', etiqueta: 'ETQ-001', numeroPedido: 'PED-1', posicion: 1, numeroPieza: 1, sucursalId: 'suc-1', estado: 'EnTransito', escaneadaAt: '2026-10-20T18:00:00Z', sucursalDestinoId: 'suc-1' },
        ],
        total: 1,
        avance: [],
      }),
    );
    render(<PiezasPage />);
    expect(screen.getAllByText('En tránsito a Conkal').length).toBeGreaterThan(0);
    expect(screen.queryByText('En PT')).not.toBeInTheDocument();
  });

  it.each([
    ['Retenido', 'SaldoPendiente', 'Saldo pendiente'],
    ['Liberado', 'SaldoCubierto', 'Saldo cubierto'],
    ['Liberado', 'Reparto', 'Pedido de reparto'],
    ['Liberado', 'DecisionCxc', 'Decisión de Crédito'],
  ])('muestra el estado %s del pedido con su motivo %s', (estado, motivo, texto) => {
    state.usePiezas.mockReturnValue(
      ok({ items: [], total: 0, avance: [], estadoPedido: { estado, motivo, desde: '2026-10-31T18:00:00Z' } }),
    );
    const { container } = render(<PiezasPage />);
    expect(screen.queryByLabelText('Estado del pedido')).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Pedido'), { target: { value: 'PED-1' } });
    const seccion = screen.getByLabelText('Estado del pedido');
    expect(seccion).toHaveTextContent(estado);
    expect(seccion).toHaveTextContent(texto);
    expect(container.textContent).not.toContain('69');
  });

  it('con posiciones pero sin estado (incompleto) muestra En producción', () => {
    const avance = [{ posicion: 1, descripcion: null, esperadas: 10, terminadas: 8, enPt: 8, cargadas: 0, entregadas: 0, pendientes: 2, avanceFisico: 'Parcial' }];
    state.usePiezas.mockReturnValue(ok({ items: [], total: 0, avance, estadoPedido: null }));
    render(<PiezasPage />);
    fireEvent.change(screen.getByLabelText('Pedido'), { target: { value: 'PED-1' } });
    expect(screen.getByLabelText('Estado del pedido')).toHaveTextContent('En producción');
  });

  it('sin estado de pedido ni posiciones no muestra la sección', () => {
    state.usePiezas.mockReturnValue(ok({ items: [], total: 0, avance: [], estadoPedido: null }));
    render(<PiezasPage />);
    fireEvent.change(screen.getByLabelText('Pedido'), { target: { value: 'PED-1' } });
    expect(screen.queryByLabelText('Estado del pedido')).not.toBeInTheDocument();
  });

  it('muestra error con reintento', () => {
    state.usePiezas.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: state.refetch });
    render(<PiezasPage />);
    fireEvent.click(screen.getByRole('button', { name: /reintentar/i }));
    expect(state.refetch).toHaveBeenCalled();
  });
});
