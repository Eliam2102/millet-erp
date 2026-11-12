import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { PiezasPage } from './PiezasPage';

const h = vi.hoisted(() => ({ piezas: vi.fn(), ubicar: vi.fn(), parcial: vi.fn() }));

vi.mock('./TramoDetenidoBanner', () => ({ TramoDetenidoBanner: () => null }));
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock('@/components/erp', async (importOriginal) => ({ ...(await importOriginal<typeof import('@/components/erp')>()), Breadcrumbs: () => null }));
vi.mock('@/components/erp/selectors/SucursalSelector', () => ({ SucursalSelector: () => null }));
vi.mock('@/features/catalogos/api', () => ({ useSucursales: () => ({ data: { items: [{ id: 'suc-1', nombre: 'Conkal' }] } }) }));
vi.mock('../api/hooks', () => ({
  usePiezas: h.piezas,
  useUbicarPiezas: () => ({ mutateAsync: h.ubicar, isPending: false }),
  useSolicitarSalidaParcial: () => ({ mutateAsync: h.parcial, isPending: false }),
}));

const pieza = (p: object) => ({ id: 'p', etiqueta: 'E', numeroPedido: 'PED-1', posicion: 1, numeroPieza: 1, sucursalId: 'suc-1', estado: 'EnPT', escaneadaAt: '2026-11-10T10:00:00Z', ...p });

beforeEach(() => {
  vi.clearAllMocks();
  h.ubicar.mockResolvedValue([]);
  h.parcial.mockResolvedValue({});
  useAuthStore.setState({ permisos: [P.ProductoTerminadoPiezasVer, P.ProductoTerminadoExcepcionesGestionar] });
  h.piezas.mockReturnValue({
    data: {
      items: [
        pieza({ id: 'p1', etiqueta: 'ETQ-1', porUbicar: true, estacionOrigen: 49, areaOrigen: 'Flotados', terminadaAwAt: '2026-11-10T09:00:00Z', costoPendiente: true }),
        pieza({ id: 'p2', etiqueta: 'ETQ-2', almacen: 'Exportacion', lugar: 'Rack B' }),
        pieza({ id: 'p3', etiqueta: 'ETQ-3', estado: 'Entregada' }),
      ],
      total: 3, avance: [],
    },
    isLoading: false, isError: false, refetch: vi.fn(),
  });
});

describe('PiezasPage · ubicación, origen y salida parcial', () => {
  it('muestra Por ubicar, el almacén y lugar, la estación de origen y Costo pendiente', () => {
    render(<PiezasPage />);
    expect(screen.getByText('Por ubicar')).toBeInTheDocument();
    expect(screen.getByText('Exportación · Rack B')).toBeInTheDocument();
    expect(screen.getByText(/Estación 49/).parentElement).toHaveTextContent('Estación 49 · Flotados');
    expect(screen.getByText('Costo pendiente')).toBeInTheDocument();
  });

  it('solo las piezas En PT se pueden seleccionar y Ubicar manda almacén y lugar', async () => {
    render(<PiezasPage />);
    expect(screen.queryByLabelText('Seleccionar ETQ-3')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ubicar' })).toBeDisabled();
    fireEvent.click(screen.getByLabelText('Seleccionar ETQ-1'));
    fireEvent.click(screen.getByLabelText('Seleccionar ETQ-2'));
    fireEvent.click(screen.getByRole('button', { name: 'Ubicar (2)' }));
    const enviar = screen.getAllByRole('button', { name: 'Ubicar' }).at(-1)!;
    expect(enviar).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Almacén'), { target: { value: 'Nacional' } });
    fireEvent.change(screen.getByLabelText('Lugar (opcional)'), { target: { value: 'Pirámide 4' } });
    fireEvent.click(enviar);
    await waitFor(() => expect(h.ubicar).toHaveBeenCalledWith({ etiquetas: ['ETQ-1', 'ETQ-2'], almacen: 'Nacional', lugar: 'Pirámide 4' }));
  });

  it('Solicitar salida parcial solo con un pedido filtrado y envía las piezas En PT con el motivo', async () => {
    render(<PiezasPage />);
    expect(screen.queryByRole('button', { name: 'Solicitar salida parcial' })).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Pedido'), { target: { value: 'PED-1' } });
    fireEvent.click(screen.getByRole('button', { name: 'Solicitar salida parcial' }));
    const enviar = screen.getByRole('button', { name: 'Enviar solicitud' });
    expect(enviar).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Motivo'), { target: { value: 'La obra espera las primeras piezas' } });
    fireEvent.click(enviar);
    await waitFor(() => expect(h.parcial).toHaveBeenCalledWith({ numeroPedido: 'PED-1', etiquetas: ['ETQ-1', 'ETQ-2'], motivo: 'La obra espera las primeras piezas' }));
  });

  it('sin permiso de gestión no hay selección ni botones', () => {
    useAuthStore.setState({ permisos: [P.ProductoTerminadoPiezasVer] });
    render(<PiezasPage />);
    expect(screen.queryByLabelText('Seleccionar ETQ-1')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Ubicar/ })).not.toBeInTheDocument();
  });
});
