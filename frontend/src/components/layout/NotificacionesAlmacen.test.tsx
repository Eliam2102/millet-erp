import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { NotificacionesAlmacen } from '@/components/layout/NotificacionesAlmacen';
import { useAuthStore } from '@/lib/auth/auth-store';

const m = vi.hoisted(() => ({ avisos: [] as unknown[], marcar: vi.fn() }));
vi.mock('@/features/almacen/api/useAvisosAlmacen', () => ({
  useAvisosAlmacen: () => ({ data: m.avisos }),
  useMarcarAvisoLeido: () => ({ mutate: m.marcar }),
}));
vi.mock('@tanstack/react-router', () => ({ Link: ({ children }: { children: React.ReactNode }) => <a href="/x">{children}</a> }));

describe('NotificacionesAlmacen', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useAuthStore.setState({ user: { id: 'u-1', email: 'a@b.c', nombre: 'U' }, currentEmpresaId: 'e-1' });
  });

  it('no aparece si el usuario no tiene avisos', () => {
    m.avisos = [];
    const { container } = render(<NotificacionesAlmacen />);
    expect(container).toBeEmptyDOMElement();
  });

  it('cuenta los avisos sin leer y los lista con su mensaje', () => {
    m.avisos = [
      { id: 'a1', tipo: 'ajuste-mayor', titulo: 'Ajuste de inventario mayor aprobado: CNT-1A2B3C4D', mensaje: 'Ajuste neto de $12,000.00 MXN', ruta: '/almacen/inventarios', creadoAt: '2026-11-05T15:00:00Z', leido: false },
      { id: 'a2', tipo: 'vale-vencido', titulo: 'Vale urgente M-VAL-000012 sin regularizar', mensaje: 'Venció sin requisición', ruta: null, creadoAt: '2026-11-04T15:00:00Z', leido: true },
    ];
    render(<NotificacionesAlmacen />);
    fireEvent.click(screen.getByRole('button', { name: 'Avisos de Almacén: 1 sin leer' }));
    expect(screen.getByText(/CNT-1A2B3C4D/)).toBeInTheDocument();
    expect(screen.getByText(/M-VAL-000012/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Marcar leído' }));
    expect(m.marcar).toHaveBeenCalledWith('a1');
  });
});
