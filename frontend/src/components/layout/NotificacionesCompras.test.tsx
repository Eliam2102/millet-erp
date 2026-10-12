import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { NotificacionesCompras } from './NotificacionesCompras';

const mocks = vi.hoisted(() => ({ query: vi.fn() }));
vi.mock('@tanstack/react-query', async (importOriginal) => ({ ...(await importOriginal<typeof import('@tanstack/react-query')>()), useQuery: (opts: unknown) => mocks.query(opts) }));
vi.mock('@tanstack/react-router', () => ({ Link: ({ children, onClick }: { children: React.ReactNode; onClick?: () => void }) => <a href="#oc" onClick={onClick}>{children}</a> }));
afterEach(() => { vi.clearAllMocks(); useAuthStore.setState({ permisos: [] }); });
const pending = { data: { items: [{ id: 'oc-1', folio: 'OC-MTY2026-000001', estado: 1 }], totalCount: 1 }, isPending: false, isError: false };

describe('Avisos de autorización de Compras', () => {
  it('habilita la campana y abre el pendiente del jefe de compras', () => {
    useAuthStore.setState({ permisos: [PermisosCanonicos.ComprasOrdenesLeer, PermisosCanonicos.ComprasOrdenesAutorizarNivel1] });
    mocks.query.mockReturnValue(pending);
    render(<NotificacionesCompras />);
    fireEvent.click(screen.getByRole('button', { name: 'Notificaciones: 1 órdenes por autorizar' }));
    expect(screen.getByText('OC-MTY2026-000001')).toBeInTheDocument();
    expect(screen.getByText('Requiere firma de Compras')).toBeInTheDocument();
  });
  it('oculta resultados anteriores si el usuario pierde el permiso de autorización', () => {
    useAuthStore.setState({ permisos: [] });
    mocks.query.mockReturnValue(pending);
    render(<NotificacionesCompras />);
    fireEvent.click(screen.getByRole('button', { name: 'Notificaciones' }));
    expect(screen.queryByText('OC-MTY2026-000001')).not.toBeInTheDocument();
    expect(screen.getByText('No tienes órdenes pendientes de autorización.')).toBeInTheDocument();
  });
  it('muestra el fallo de consulta sin fingir que no hay pendientes', () => {
    useAuthStore.setState({ permisos: [PermisosCanonicos.ComprasOrdenesLeer, PermisosCanonicos.ComprasOrdenesAutorizarNivel1] });
    mocks.query.mockReturnValue({ isError: true, isPending: false });
    render(<NotificacionesCompras />);
    fireEvent.click(screen.getByRole('button', { name: 'Notificaciones' }));
    expect(screen.getByRole('alert')).toHaveTextContent('No se pudieron consultar los avisos');
  });
});
