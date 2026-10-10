import { fireEvent, render, screen, within } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import type { ComponentProps } from 'react';
import { SidebarNav } from './SidebarNav';
import { Sidebar } from './Sidebar';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ to, children, ...props }: ComponentProps<'a'> & { to: string }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
  useLocation: () => ({ pathname: '/compras/ordenes' }),
}));
vi.mock('@/lib/auth/useAuth', () => ({
  useAuth: () => ({
    user: { nombre: 'Persona de prueba' },
    currentEmpresa: { razonSocial: 'Empresa de prueba' },
    logout: vi.fn(),
  }),
}));
vi.mock('@/components/auth/EmpresaSelector', () => ({ EmpresaSelector: () => null }));
vi.mock('@/components/auth/SucursalSelector', () => ({ SucursalSelector: () => null }));

beforeEach(() => useAuthStore.setState({ permisos: [] }));
it('muestra solo módulos y pantallas permitidos y mantiene el gate any-of', () => {
  useAuthStore.setState({
    permisos: [P.ComprasRequisicionesLeer, P.ComprasOrdenesAutorizarNivel2],
  });
  const { container } = render(<SidebarNav />);
  expect(screen.getByRole('link', { name: 'Compras' })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Mis requisiciones' })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Órdenes por autorizar' })).toBeInTheDocument();
  expect(screen.queryByRole('link', { name: 'Órdenes de compra' })).not.toBeInTheDocument();
  expect(screen.queryByText('Almacén')).not.toBeInTheDocument();
  expect(container.querySelector('details, summary, [aria-expanded]')).toBeNull();
});
it('filtra una pantalla por nombre sin abrir nada y conserva su selección', () => {
  useAuthStore.setState({ permisos: [P.ComprasOrdenesLeer, P.ComprasRequisicionesLeer] });
  render(<SidebarNav />);
  fireEvent.change(screen.getByRole('textbox', { name: 'Filtrar menú' }), {
    target: { value: 'ordenes de compra' },
  });
  expect(screen.getByRole('link', { name: 'Órdenes de compra' })).toHaveAttribute(
    'aria-current',
    'page',
  );
  expect(screen.queryByText('Mis requisiciones')).not.toBeInTheDocument();
});
it('nombres completos, un solo acceso a Administración y configuración fuera del menú', () => {
  useAuthStore.setState({ permisos: Object.values(P) });
  const { container } = render(<SidebarNav />);
  expect(container.querySelectorAll('a[href="/admin"]')).toHaveLength(1);
  for (const name of ['Cuentas por pagar', 'Cobranza', 'Centros de costo', 'Contabilidad'])
    expect(screen.getAllByRole('link', { name }).length).toBeGreaterThan(0);
  expect(screen.queryByText(/^(CxP|CxC|C\. Costo|Contab\.|Admin)$/)).not.toBeInTheDocument();
  expect(screen.queryByRole('link', { name: 'Cajas' })).not.toBeInTheDocument();
  expect(screen.queryByRole('link', { name: 'Aprobadores' })).not.toBeInTheDocument();
  expect(screen.queryByText('Configuración')).not.toBeInTheDocument();
  expect(container.firstElementChild).toHaveAttribute('data-print', 'hidden');
});
it('datos maestros están en su módulo con el permiso original', () => {
  useAuthStore.setState({
    permisos: [
      P.DatosMaestrosProveedoresGestionar,
      P.DatosMaestrosArticulosGestionar,
      P.DatosMaestrosClientesGestionar,
      P.DatosMaestrosProductosAwGestionar,
    ],
  });
  render(<SidebarNav />);
  const compras = within(screen.getByRole('region', { name: 'Abastecimiento' }));
  expect(compras.getByRole('link', { name: 'Proveedores' })).toHaveAttribute(
    'href',
    '/admin/datos-maestros/proveedores',
  );
  expect(compras.getByRole('link', { name: 'Artículos' })).toBeInTheDocument();
  const ventas = within(screen.getByRole('region', { name: 'Ventas' }));
  expect(ventas.getByRole('link', { name: 'Clientes' })).toBeInTheDocument();
  expect(ventas.getByRole('link', { name: 'Productos A+W' })).toBeInTheDocument();
  expect(screen.queryByRole('link', { name: 'Administración' })).not.toBeInTheDocument();
});
it('64 px al contraer, nombres accesibles y tooltip; expande con el botón', () => {
  useAuthStore.setState({ permisos: [P.ComprasRequisicionesLeer] });
  const toggle = vi.fn();
  const view = render(<Sidebar collapsed={false} onToggle={toggle} contadores={{}} />);
  fireEvent.click(screen.getByRole('button', { name: 'Contraer menú' }));
  expect(toggle).toHaveBeenCalledOnce();
  view.rerender(<Sidebar collapsed onToggle={toggle} contadores={{}} />);
  expect(screen.getByRole('link', { name: 'Compras' })).toBeInTheDocument();
  expect(screen.queryByText('Mis requisiciones')).not.toBeInTheDocument();
  expect(view.container.querySelector('.w-menu-collapsed')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Expandir menú' }));
  expect(toggle).toHaveBeenCalledTimes(2);
});
it('muestra un conteo disponible y no agrega números a los otros módulos', () => {
  useAuthStore.setState({ permisos: [P.ComprasRequisicionesLeer, P.AlmacenEntradasLeer] });
  render(<SidebarNav contadores={{ Compras: 3 }} />);
  expect(screen.getByLabelText('3 pendientes en Compras')).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Almacén' })).not.toHaveTextContent(/\d/);
});
it('muestra la persona sin inventar un rol ni dejar un marcador cuando la sesión no lo trae', () => {
  useAuthStore.setState({ permisos: Object.values(P) });
  render(<SidebarNav />);
  expect(screen.getByText('Persona de prueba')).toBeInTheDocument();
  expect(screen.queryByText('[ROL]')).not.toBeInTheDocument();
  expect(screen.queryByText('Administrador')).not.toBeInTheDocument();
});
