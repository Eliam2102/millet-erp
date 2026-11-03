import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { SalidasPendientesPage } from './SalidasPendientesPage';

const state = vi.hoisted(() => ({ hook: vi.fn(), refetch: vi.fn() }));

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
vi.mock('../api/hooks', () => ({ useSalidasPendientes: state.hook }));

const ok = (data: unknown) => ({ data, isLoading: false, isError: false, refetch: state.refetch });
const fila = (id: string, alerta: string, horas: number) => ({
  pedidoPtId: id, numeroPedido: `PED-${id}`, cliente: 'Vidrios del Sur', destino: 'Mérida', sucursalId: 'suc-1',
  piezas: 4, liberadoAt: '2026-10-20T18:00:00Z', horasSinSalida: horas, alerta,
});

beforeEach(() => vi.clearAllMocks());

describe('SalidasPendientesPage', () => {
  it('muestra estado vacío', () => {
    state.hook.mockReturnValue(ok({ items: [], total: 0, con24h: 0, con48h: 0 }));
    render(<SalidasPendientesPage />);
    expect(screen.getByText('No hay pedidos pendientes de salir.')).toBeInTheDocument();
  });

  it('muestra error con reintento', () => {
    state.hook.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch: state.refetch });
    render(<SalidasPendientesPage />);
    fireEvent.click(screen.getByRole('button', { name: /reintentar/i }));
    expect(state.refetch).toHaveBeenCalled();
  });

  it('muestra filas, edad y badges con texto', () => {
    state.hook.mockReturnValue(ok({ items: [fila('1', '24h', 30.4), fila('2', '48h', 50)], total: 2, con24h: 1, con48h: 1 }));
    render(<SalidasPendientesPage />);
    expect(screen.getAllByText('PED-1').length).toBeGreaterThan(0);
    expect(screen.getAllByText('hace 30 h').length).toBeGreaterThan(0);
    expect(screen.getAllByText('24 h').length).toBeGreaterThan(0);
    expect(screen.getAllByText('48 h o más').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Conkal').length).toBeGreaterThan(0);
  });

  it('filtra por alerta desde el contador y por búsqueda', () => {
    state.hook.mockReturnValue(ok({ items: [fila('1', '48h', 50)], total: 1, con24h: 3, con48h: 1 }));
    render(<SalidasPendientesPage />);
    const c = screen.getByRole('button', { name: /48 h o más/ });
    fireEvent.click(c);
    expect(state.hook).toHaveBeenLastCalledWith(expect.objectContaining({ alerta: '48h' }));
    expect(c).toHaveAttribute('aria-pressed', 'true');
    fireEvent.click(c);
    expect(state.hook).toHaveBeenLastCalledWith(expect.objectContaining({ alerta: undefined }));
    fireEvent.change(screen.getByLabelText('Buscar pedido o cliente'), { target: { value: 'Sur' } });
    expect(state.hook).toHaveBeenLastCalledWith(expect.objectContaining({ buscar: 'Sur' }));
  });
});
