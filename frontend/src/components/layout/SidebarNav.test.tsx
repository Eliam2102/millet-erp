import { render, screen } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { SidebarNav } from './SidebarNav';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
vi.mock('@tanstack/react-router', () => ({
  useLocation: () => ({ pathname: '/compras/ordenes/oc-123' }),
  Link: ({ to, children, ...props }: { to: string; children: React.ReactNode }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
}));
it('conserva Inicio primero y pone Compras antes de los módulos complementarios', () => {
  useAuthStore.setState({ permisos: Object.values(P) });
  const { container } = render(<SidebarNav compact onModuloOpen={vi.fn()} />);
  const rail = screen.getByRole('navigation', { name: 'Módulos' });
  expect(rail.firstElementChild).toHaveTextContent('Inicio');
  const botones = container.querySelectorAll('button');
  expect(botones[0]).toHaveAccessibleName('Compras');
  expect(screen.getByRole('button', { name: 'Cuentas por Pagar' })).toHaveAttribute(
    'title',
    'Cuentas por Pagar',
  );
});
