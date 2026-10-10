import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import type { ComponentProps } from 'react';
import { MobileSidebar } from './MobileSidebar';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, ...props }: ComponentProps<'a'> & { to: string }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
  useLocation: () => ({ pathname: '/' }),
}));
vi.mock('@/lib/auth/useAuth', () => ({ useAuth: () => ({ logout: vi.fn() }) }));
vi.mock('@/components/auth/EmpresaSelector', () => ({ EmpresaSelector: () => null }));
vi.mock('@/components/auth/SucursalSelector', () => ({ SucursalSelector: () => null }));
beforeEach(() => useAuthStore.setState({ permisos: [P.ComprasRequisicionesLeer] }));
it('cerrado no renderiza el diálogo', () => {
  render(<MobileSidebar open={false} onOpenChange={vi.fn()} />);
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
});
it('muestra pantallas directamente y navega al cerrar, sin launcher', () => {
  const close = vi.fn();
  render(<MobileSidebar open onOpenChange={close} />);
  expect(screen.getByRole('dialog', { name: 'Navegación principal' })).toBeInTheDocument();
  fireEvent.click(screen.getByRole('link', { name: 'Mis requisiciones' }));
  expect(close).toHaveBeenCalledWith(false);
  expect(screen.getByRole('link', { name: 'Mis requisiciones' })).toHaveAttribute(
    'href',
    '/compras/requisiciones',
  );
  expect(screen.queryByRole('tab')).not.toBeInTheDocument();
});
it('botón de cierre y módulos sin permiso', () => {
  const close = vi.fn();
  useAuthStore.setState({ permisos: [] });
  render(<MobileSidebar open onOpenChange={close} />);
  expect(screen.queryByText('Compras')).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Cerrar navegación' }));
  expect(close).toHaveBeenCalledWith(false);
});
