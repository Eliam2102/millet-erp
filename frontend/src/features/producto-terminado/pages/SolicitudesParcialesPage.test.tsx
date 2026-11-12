import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { SolicitudesParcialesPage } from './SolicitudesParcialesPage';

const h = vi.hoisted(() => ({ lista: vi.fn(), resolver: vi.fn() }));

vi.mock('./TramoDetenidoBanner', () => ({ TramoDetenidoBanner: () => null }));
vi.mock('../lib/usuarios', () => ({ useNombreUsuario: () => (id?: string | null) => id ?? '—' }));
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock('@/components/erp', async (importOriginal) => ({ ...(await importOriginal<typeof import('@/components/erp')>()), Breadcrumbs: () => null }));
vi.mock('@/features/catalogos/api', () => ({ useSucursales: () => ({ data: { items: [{ id: 'suc-1', nombre: 'Conkal' }] } }) }));
vi.mock('../api/hooks', () => ({ useSolicitudesParciales: h.lista, useResolverSalidaParcial: () => ({ mutateAsync: h.resolver, isPending: false }) }));

const sol = (p: object = {}) => ({
  id: 's1', numeroPedido: 'PED-5', sucursalId: 'suc-1', etiquetas: ['E1', 'E2'], esperadas: 6, enPt: 2, motivo: 'El cliente necesita lo urgente', estado: 'Pendiente',
  solicitadaPorId: 'Laura', solicitadaAt: '2026-11-10T10:00:00Z', nivel: null, suplencia: null, motivoResolucion: null, resueltaPorId: null, resueltaAt: null, ...p,
});
const ok = (items: object[]) => h.lista.mockReturnValue({ data: { items, total: items.length }, isLoading: false, isError: false, refetch: vi.fn() });

beforeEach(() => {
  vi.clearAllMocks();
  h.resolver.mockResolvedValue({});
  useAuthStore.setState({ permisos: [P.ProductoTerminadoPiezasVer, P.ProductoTerminadoSalidaAprobarCredito] });
});

describe('SolicitudesParcialesPage (M-245)', () => {
  it('filtra por pendientes por omisión y muestra vacío', () => {
    ok([]);
    render(<SolicitudesParcialesPage />);
    expect(h.lista).toHaveBeenLastCalledWith(expect.objectContaining({ estado: 'Pendiente' }));
    expect(screen.getByText('No hay solicitudes para esos filtros')).toBeInTheDocument();
  });

  it('Crédito autoriza con su motivo', async () => {
    ok([sol()]);
    render(<SolicitudesParcialesPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar' }));
    fireEvent.change(screen.getByLabelText('Motivo de la autorización'), { target: { value: 'Pago al corriente' } });
    fireEvent.click(screen.getAllByRole('button', { name: 'Autorizar' }).at(-1)!);
    await waitFor(() => expect(h.resolver).toHaveBeenCalledWith({ id: 's1', body: { autorizar: true, motivo: 'Pago al corriente', nivel: 'credito' } }));
  });

  it('Gerencia de Ventas como suplente exige el motivo de la suplencia', async () => {
    useAuthStore.setState({ permisos: [P.ProductoTerminadoPiezasVer, P.ProductoTerminadoSalidaAprobarVentas] });
    ok([sol()]);
    render(<SolicitudesParcialesPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Autorizar' }));
    fireEvent.change(screen.getByLabelText('Motivo de la autorización'), { target: { value: 'Urgente para la obra' } });
    const confirmar = screen.getAllByRole('button', { name: 'Autorizar' }).at(-1)!;
    expect(confirmar).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/Por qué Crédito y Cobranza no pudo/), { target: { value: 'La gerente está de viaje' } });
    fireEvent.click(confirmar);
    await waitFor(() => expect(h.resolver).toHaveBeenCalledWith({ id: 's1', body: { autorizar: true, motivo: 'Urgente para la obra', nivel: 'ventas', suplencia: 'La gerente está de viaje' } }));
  });

  it('rechazar no manda nivel y sin permiso no hay acciones', async () => {
    ok([sol()]);
    const { unmount } = render(<SolicitudesParcialesPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Rechazar' }));
    fireEvent.change(screen.getByLabelText('Motivo del rechazo'), { target: { value: 'Tiene saldo vencido' } });
    fireEvent.click(screen.getAllByRole('button', { name: 'Rechazar' }).at(-1)!);
    await waitFor(() => expect(h.resolver).toHaveBeenCalledWith({ id: 's1', body: { autorizar: false, motivo: 'Tiene saldo vencido' } }));
    unmount();
    useAuthStore.setState({ permisos: [P.ProductoTerminadoPiezasVer] });
    render(<SolicitudesParcialesPage />);
    expect(screen.queryByRole('button', { name: 'Autorizar' })).not.toBeInTheDocument();
    expect(screen.getByText('Sin permiso para resolver')).toBeInTheDocument();
  });

  it('una solicitud resuelta muestra quién, el nivel y el motivo, sin acciones', () => {
    ok([sol({ estado: 'Autorizada', nivel: 'ventas', suplencia: 'Gerente de viaje', motivoResolucion: 'Urgente', resueltaPorId: 'Pedro', resueltaAt: '2026-11-10T12:00:00Z' })]);
    render(<SolicitudesParcialesPage />);
    expect(screen.getByText('Autorizada')).toBeInTheDocument();
    expect(screen.getByText(/Gerencia de Ventas \(suplencia\)/)).toBeInTheDocument();
    expect(screen.getByText(/Gerente de viaje/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Rechazar' })).not.toBeInTheDocument();
  });
});
