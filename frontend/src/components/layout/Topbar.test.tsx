import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import { Topbar } from './Topbar';
const access = vi.hoisted(() => ({ admin: true }));
vi.mock('@tanstack/react-router', () => ({
  useLocation: () => ({ pathname: '/compras/requisiciones/nueva', search: {} }),
  useNavigate: () => vi.fn(),
  Link: ({ to, children, ...props }: { to: string; children: ReactNode }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
}));
vi.mock('@/lib/admin/use-admin-registry', () => ({ useAdminAccess: () => access.admin }));
vi.mock('@/components/auth/EmpresaSelector', () => ({
  EmpresaSelector: () => <select aria-label="Empresa activa" />,
}));
vi.mock('@/components/auth/SucursalSelector', () => ({
  SucursalSelector: () => <select aria-label="Sucursal activa" />,
}));
vi.mock('./AccessSearch', () => ({ AccessSearch: () => <button>Buscar o ir a</button> }));
vi.mock('./QuickCreateMenu', () => ({ QuickCreateMenu: () => <button>Crear nuevo</button> }));
vi.mock('./UserMenu', () => ({ UserMenu: () => <button>Menú de usuario</button> }));
vi.mock('@/features/notificaciones/components/CampanaAvisos', () => ({
  CampanaAvisos: () => <button>Notificaciones</button>,
}));
beforeEach(() => {
  access.admin = true;
});
it('mantiene menú, contexto, búsqueda, avisos y usuario y abre herramientas secundarias', () => {
  const onMenuClick = vi.fn();
  render(<Topbar onMenuClick={onMenuClick} />);
  fireEvent.click(screen.getByRole('button', { name: 'Abrir navegación' }));
  expect(onMenuClick).toHaveBeenCalledOnce();
  for (const name of ['Buscar o ir a', 'Notificaciones', 'Menú de usuario'])
    expect(screen.getByRole('button', { name })).toBeInTheDocument();
  expect(screen.getByLabelText('Empresa activa')).toBeInTheDocument();
  expect(screen.getByLabelText('Sucursal activa')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Más opciones' }));
  const tools = screen.getByRole('dialog', { name: 'Herramientas' });
  expect(tools).toHaveTextContent('Crear nuevo');
  expect(tools).toHaveTextContent('Ayuda');
  expect(tools).toHaveTextContent('Configuración');
});
it('no ofrece Administración desde herramientas si no tiene acceso', () => {
  access.admin = false;
  render(<Topbar />);
  fireEvent.click(screen.getByRole('button', { name: 'Más opciones' }));
  expect(screen.queryByRole('link', { name: 'Configuración' })).not.toBeInTheDocument();
});

it('mantiene limpia la cabecera sin el nombre Millet ERP', () => {
  render(<Topbar />);
  expect(screen.queryByText('Millet ERP')).not.toBeInTheDocument();
});
