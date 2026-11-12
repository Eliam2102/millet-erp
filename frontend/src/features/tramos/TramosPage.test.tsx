import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { TramosPage } from './TramosPage';

const h = vi.hoisted(() => ({ tramos: vi.fn(), accion: vi.fn() }));

vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock('@/components/erp', async (importOriginal) => ({ ...(await importOriginal<typeof import('@/components/erp')>()), Breadcrumbs: () => null }));
vi.mock('@/features/producto-terminado/lib/usuarios', () => ({ useNombreUsuario: () => (id?: string | null) => id ?? '—' }));
vi.mock('./api', () => ({ useTramos: h.tramos, useAccionTramo: () => ({ mutateAsync: h.accion, isPending: false }) }));

const tramo = (p: object = {}) => ({
  clave: 'pt.captura', nombre: 'Captura de producto terminado', modulo: 'Producto terminado', detenido: false, motivo: null, detenidoPorId: null, detenidoAt: null, incidenciaId: null,
  ultimoEventoCorrectoAt: '2026-11-11T08:00:00Z', ultimoEventoCorrecto: 'Alta de pieza ETQ-9', reanudadoPorId: null, reanudadoAt: null, motivoReanudacion: null, ...p,
});
const ok = (data: object[]) => h.tramos.mockReturnValue({ data, isLoading: false, isError: false, refetch: vi.fn() });

beforeEach(() => { vi.clearAllMocks(); h.accion.mockResolvedValue({}); });

describe('TramosPage (M-303)', () => {
  it('lista el tramo con su último evento correcto y detiene con motivo', async () => {
    ok([tramo()]);
    render(<TramosPage />);
    expect(screen.getByText('Operando')).toBeInTheDocument();
    expect(screen.getByText('Alta de pieza ETQ-9')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Detener' }));
    fireEvent.change(screen.getByLabelText('Motivo de la detención'), { target: { value: 'El escáner duplica altas' } });
    fireEvent.click(screen.getAllByRole('button', { name: 'Detener' }).at(-1)!);
    await waitFor(() => expect(h.accion).toHaveBeenCalledWith({ clave: 'pt.captura', accion: 'detener', motivo: 'El escáner duplica altas' }));
  });

  it('detenido: muestra motivo y quién, y reanuda; un error del servidor queda en el diálogo', async () => {
    ok([tramo({ detenido: true, motivo: 'Altas duplicadas', detenidoPorId: 'Laura', detenidoAt: '2026-11-11T09:00:00Z' })]);
    h.accion.mockRejectedValueOnce(new Error('falló'));
    render(<TramosPage />);
    expect(screen.getByText('Detenido')).toBeInTheDocument();
    expect(screen.getByText('Altas duplicadas')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Reanudar' }));
    fireEvent.change(screen.getByLabelText('Motivo de la reanudación'), { target: { value: 'Se reemplazó el escáner' } });
    fireEvent.click(screen.getAllByRole('button', { name: 'Reanudar' }).at(-1)!);
    await waitFor(() => expect(h.accion).toHaveBeenCalledWith({ clave: 'pt.captura', accion: 'reanudar', motivo: 'Se reemplazó el escáner' }));
    // El diálogo se queda abierto para corregir; no se confirma el éxito.
    expect(await screen.findByText('Reanudar Captura de producto terminado')).toBeInTheDocument();
  });

  it('error al cargar con reintento y vacío', () => {
    const refetch = vi.fn();
    h.tramos.mockReturnValue({ data: undefined, isLoading: false, isError: true, refetch });
    const { unmount } = render(<TramosPage />);
    fireEvent.click(screen.getByRole('button', { name: /reintentar/i }));
    expect(refetch).toHaveBeenCalled();
    unmount();
    ok([]);
    render(<TramosPage />);
    expect(screen.getByText('No hay tramos configurados.')).toBeInTheDocument();
  });
});
