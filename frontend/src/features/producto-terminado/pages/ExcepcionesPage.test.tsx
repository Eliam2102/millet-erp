import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { ExcepcionesPage } from './ExcepcionesPage';

const state = vi.hoisted(() => ({
  useEscaneos: vi.fn(),
  mutateAsync: vi.fn(),
  refetch: vi.fn(),
  pending: false,
}));

vi.mock('@/components/erp', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/components/erp')>()),
  Breadcrumbs: () => null,
}));
vi.mock('../api/hooks', () => ({
  useEscaneos: state.useEscaneos,
  useEscaneoAccion: () => ({ mutateAsync: state.mutateAsync, isPending: state.pending }),
}));

const fila = (o: object = {}) => ({
  id: 'e1',
  origen: 'demo',
  etiqueta: 'ETQ-1',
  numeroPedido: 'PED-1',
  posicion: 2,
  numeroPieza: '3',
  estacion: 10,
  area: 'Corte',
  registradoAt: '2026-10-20T18:00:00Z',
  sucursalId: 's1',
  resultado: 'Excepcion',
  motivo: 'PT_PEDIDO_CANCELADO',
  intentos: 2,
  piezaId: null,
  resueltoAt: null,
  motivoResolucion: null,
  version: 7,
  ...o,
});
const lista = (items: object[]) => ({ data: { items, total: items.length }, isLoading: false, isError: false, refetch: state.refetch });

beforeEach(() => {
  vi.clearAllMocks();
  state.pending = false;
  state.mutateAsync.mockResolvedValue({});
  useAuthStore.setState({ permisos: [P.ProductoTerminadoExcepcionesGestionar] });
});

describe('ExcepcionesPage', () => {
  it('muestra estado vacío', () => {
    state.useEscaneos.mockReturnValue(lista([]));
    render(<ExcepcionesPage />);
    expect(screen.getByText('Sin excepciones abiertas')).toBeInTheDocument();
  });

  it('muestra filas con el motivo en español y el código desconocido tal cual; cambia de pestaña', () => {
    state.useEscaneos.mockReturnValue(lista([fila(), fila({ id: 'e2', etiqueta: 'ETQ-2', motivo: 'PT_RARO' })]));
    render(<ExcepcionesPage />);
    expect(screen.getByText('El pedido está cancelado')).toBeInTheDocument();
    expect(screen.getByText('PT_RARO')).toBeInTheDocument();
    fireEvent.mouseDown(screen.getByRole('tab', { name: 'Pospuesto' }));
    fireEvent.click(screen.getByRole('tab', { name: 'Pospuesto' }));
    expect(state.useEscaneos).toHaveBeenLastCalledWith(expect.objectContaining({ resultado: 'Pospuesto' }));
  });

  it('Reintentar llama a la mutación con id y versión de la fila', async () => {
    state.useEscaneos.mockReturnValue(lista([fila()]));
    render(<ExcepcionesPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    await waitFor(() =>
      expect(state.mutateAsync).toHaveBeenCalledWith({ escaneo: expect.objectContaining({ id: 'e1', version: 7 }), ruta: 'reintentar' }),
    );
  });

  it('Descartar exige motivo y llama con { motivo }', async () => {
    state.useEscaneos.mockReturnValue(lista([fila()]));
    render(<ExcepcionesPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Descartar' }));
    const confirmar = screen.getByRole('button', { name: 'Descartar', hidden: false, description: /motivo/i });
    expect(confirmar).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Motivo del descarte'), { target: { value: 'Etiqueta duplicada' } });
    fireEvent.click(confirmar);
    await waitFor(() =>
      expect(state.mutateAsync).toHaveBeenCalledWith({
        escaneo: expect.objectContaining({ id: 'e1', version: 7 }),
        ruta: 'descartar',
        body: { motivo: 'Etiqueta duplicada' },
      }),
    );
  });

  it('Marcar revisada solo aparece en Reposicion', async () => {
    state.useEscaneos.mockReturnValue(lista([fila({ resultado: 'Reposicion' })]));
    render(<ExcepcionesPage />);
    expect(screen.queryByRole('button', { name: 'Reintentar' })).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Marcar revisada' }));
    await waitFor(() => expect(state.mutateAsync).toHaveBeenCalledWith(expect.objectContaining({ ruta: 'revisar' })));
  });

  it('deshabilita los botones mientras la mutación está pendiente', () => {
    state.pending = true;
    state.useEscaneos.mockReturnValue(lista([fila()]));
    render(<ExcepcionesPage />);
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Descartar' })).toBeDisabled();
  });

  it('muestra el error de la acción y deja el botón habilitado', async () => {
    state.mutateAsync.mockRejectedValue(new Error('La versión cambió, actualice.'));
    state.useEscaneos.mockReturnValue(lista([fila()]));
    render(<ExcepcionesPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    expect(await screen.findByText('La versión cambió, actualice.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeEnabled();
  });

  it('no renderiza acciones sin permiso de gestionar', () => {
    useAuthStore.setState({ permisos: [] });
    state.useEscaneos.mockReturnValue(lista([fila()]));
    render(<ExcepcionesPage />);
    expect(screen.getByText('ETQ-1')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reintentar' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Descartar' })).toBeNull();
  });
});
