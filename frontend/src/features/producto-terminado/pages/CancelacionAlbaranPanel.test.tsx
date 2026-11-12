import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { CancelacionAlbaranPanel } from './CancelacionAlbaranPanel';
import type { AlbaranDto } from '../api/types';

const mutar = vi.hoisted(() => vi.fn());
vi.mock('../api/hooks', () => ({ useCancelacionAlbaran: () => ({ mutateAsync: mutar, isPending: false }) }));
vi.mock('../lib/usuarios', () => ({ useNombreUsuario: () => (id?: string | null) => (id === 'u1' ? 'Mariana Pech' : id ?? '—') }));
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const albaran = (parche: Partial<AlbaranDto> = {}) =>
  ({ id: 'a1', folio: 'HS/1', secuencia: 1, numeroPedido: '260317', cliente: 'Cliente', destino: null, emitidoAt: null, total: 2, lineas: [], etiquetas: ['E1'], ...parche }) as AlbaranDto;

beforeEach(() => {
  vi.clearAllMocks();
  mutar.mockResolvedValue({});
  useAuthStore.setState({ permisos: [] });
});

describe('CancelacionAlbaranPanel (M-247)', () => {
  it('con la entrega confirmada no ofrece cancelar y manda a Corregir entrega o Caso de daño', () => {
    render(<CancelacionAlbaranPanel hojaId="h1" operar albaran={albaran({ entrega: { id: 'e1' } as never })} />);
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
    expect(screen.getByText(/Corregir entrega/)).toBeInTheDocument();
    expect(screen.getByText(/Caso de daño/)).toBeInTheDocument();
  });

  it('solicitar pide el motivo y llama al paso solicitar-cancelacion', async () => {
    render(<CancelacionAlbaranPanel hojaId="h1" operar albaran={albaran()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Solicitar cancelación' }));
    fireEvent.change(screen.getByLabelText('Motivo de la cancelación'), { target: { value: 'El cliente pospuso la obra' } });
    fireEvent.click(screen.getByRole('button', { name: 'Solicitar' }));
    await waitFor(() => expect(mutar).toHaveBeenCalledWith({ hojaId: 'h1', albaranId: 'a1', paso: 'solicitar-cancelacion', body: { motivo: 'El cliente pospuso la obra' } }));
  });

  it('pendiente: muestra quién la pidió y, con permiso, autoriza; sin permiso solo avisa', async () => {
    const a = albaran({ cancelacion: { pendiente: true, solicitadaAt: '2026-11-10T10:00:00Z', solicitadaPorId: 'u1', motivoSolicitud: 'Obra pospuesta', rechazadaAt: null, rechazadaPorId: null, motivoRechazo: null, autorizadaPorId: null } });
    const { unmount } = render(<CancelacionAlbaranPanel hojaId="h1" operar albaran={a} />);
    expect(screen.getByText(/Mariana Pech/)).toBeInTheDocument();
    expect(screen.getByText(/Obra pospuesta/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Autorizar' })).not.toBeInTheDocument();
    unmount();

    useAuthStore.setState({ permisos: [P.ProductoTerminadoSalidaCancelarEmitida] });
    render(<CancelacionAlbaranPanel hojaId="h1" operar albaran={a} />);
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar' }));
    fireEvent.change(screen.getByLabelText('Motivo de la autorización'), { target: { value: 'Confirmado con el cliente' } });
    fireEvent.click(screen.getAllByRole('button', { name: 'Autorizar' }).at(-1)!);
    await waitFor(() => expect(mutar).toHaveBeenCalledWith({ hojaId: 'h1', albaranId: 'a1', paso: 'resolver-cancelacion', body: { autorizar: true, motivo: 'Confirmado con el cliente' } }));
  });

  it('muestra el rechazo previo en el historial y deja volver a solicitar', () => {
    render(<CancelacionAlbaranPanel hojaId="h1" operar albaran={albaran({ cancelacion: { pendiente: false, solicitadaAt: '2026-11-10T10:00:00Z', solicitadaPorId: 'u1', motivoSolicitud: 'Obra pospuesta', rechazadaAt: '2026-11-10T12:00:00Z', rechazadaPorId: 'u2', motivoRechazo: 'Ya está cargado', autorizadaPorId: null } })} />);
    expect(screen.getByText(/Cancelación rechazada por u2/)).toHaveTextContent('Ya está cargado');
    expect(screen.getByRole('button', { name: 'Solicitar cancelación' })).toBeInTheDocument();
  });
});
