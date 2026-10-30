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
        avance: [{ posicion: 2, descripcion: null, esperadas: 5, terminadas: 3, faltantes: 2, avanceFisico: 'Parcial' }],
      }),
    );
    const { container } = render(<PiezasPage />);
    fireEvent.change(screen.getByLabelText('Pedido'), { target: { value: 'PED-1' } });
    fireEvent.change(screen.getByLabelText('Sucursal'), { target: { value: 'suc-1' } });
    expect(state.usePiezas).toHaveBeenLastCalledWith(expect.objectContaining({ pedido: 'PED-1', sucursalId: 'suc-1' }));
    expect(screen.getByText('ETQ-001')).toBeInTheDocument();
    expect(screen.getAllByText('Conkal').length).toBeGreaterThan(0);
    expect(screen.getByText('En PT')).toBeInTheDocument();
    expect(screen.getByText('Parcial')).toBeInTheDocument();
    expect(screen.getByText('Faltantes')).toBeInTheDocument();
    expect(container.textContent).not.toContain('69');
  });

  it('muestra error con reintento', () => {
    state.usePiezas.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: state.refetch });
    render(<PiezasPage />);
    fireEvent.click(screen.getByRole('button', { name: /reintentar/i }));
    expect(state.refetch).toHaveBeenCalled();
  });
});
