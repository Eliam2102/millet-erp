import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { AccessSearch } from './AccessSearch';
import { Topbar } from './Topbar';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { accesosNavegacion, rutaPermitida } from '@/lib/nav';
const navigate = vi.fn();
vi.mock('@tanstack/react-router', () => ({
  Link: ({ to, children }: { to: string; children: React.ReactNode }) => <a href={to}>{children}</a>,
  useNavigate: () => navigate,
  useLocation: () => ({ pathname: '/compras/ordenes', search: { page: 5 } }),
}));
beforeEach(() => {
  navigate.mockClear();
  useAuthStore.setState({ permisos: [P.ComprasOrdenesLeer], isSwitchingEmpresa: false });
});

it('abre con ⌘K, busca sin acentos y navega con Enter a una pantalla permitida', async () => {
  render(<AccessSearch />);
  fireEvent.keyDown(document, { key: 'k', metaKey: true });
  const input = await screen.findByRole('combobox', { name: 'Buscar accesos' });
  fireEvent.change(input, { target: { value: 'ordenes compra' } });
  await waitFor(() => expect(screen.getAllByRole('option')).toHaveLength(1));
  expect(screen.getByRole('option')).toHaveTextContent('Órdenes de compra');
  fireEvent.keyDown(input, { key: 'ArrowDown' });
  fireEvent.keyDown(input, { key: 'Enter' });
  await waitFor(() => expect(navigate).toHaveBeenCalledWith({ to: '/compras/ordenes' }));
});

it('actualiza accesos al cambiar permisos y muestra estado vacío', async () => {
  render(<AccessSearch />);
  fireEvent.click(screen.getByRole('button', { name: 'Buscar o ir a' }));
  const input = await screen.findByRole('combobox', { name: 'Buscar accesos' });
  fireEvent.change(input, { target: { value: 'ordenes compra' } });
  await screen.findByRole('option');
  act(() => useAuthStore.setState({ permisos: [] }));
  await waitFor(() => expect(screen.queryByRole('option')).not.toBeInTheDocument());
  expect(screen.getByText('No hay accesos que coincidan con tu búsqueda.')).toBeInTheDocument();
  expect(navigate).not.toHaveBeenCalled();
});

it('bloquea el atajo durante cambio de empresa y deduplica rutas de administración', () => {
  useAuthStore.setState({ isSwitchingEmpresa: true });
  render(<AccessSearch />);
  fireEvent.keyDown(document, { key: 'k', ctrlKey: true });
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  const accesos = accesosNavegacion(Object.values(P));
  expect(new Set(accesos.map((a) => a.to)).size).toBe(accesos.length);
  expect(accesos.some((a) => a.to === '/admin/roles')).toBe(true);
  expect(accesos.every((a) => rutaPermitida(a.to, Object.values(P)))).toBe(true);
  expect(accesosNavegacion([]).map((a) => a.to)).toEqual(['/']);
});

vi.mock('@/components/auth/EmpresaSelector', () => ({ EmpresaSelector: () => null }));
vi.mock('@/components/auth/SucursalSelector', () => ({ SucursalSelector: () => null }));
vi.mock('@/components/layout/QuickCreateMenu', () => ({ QuickCreateMenu: () => null }));
vi.mock('@/components/layout/UserMenu', () => ({ UserMenu: () => null }));
vi.mock('@/lib/admin/use-admin-registry', () => ({ useAdminAccess: () => false }));

it('conserva la búsqueda contextual separada y reinicia la paginación', async () => {
  render(<Topbar />);
  fireEvent.click(screen.getByRole('button', { name: 'Buscar o ir a' }));
  fireEvent.change(await screen.findByRole('combobox', { name: 'Buscar accesos' }), {
    target: { value: 'ordenes' },
  });
  expect(navigate).not.toHaveBeenCalled();
  const contextual = screen.getByRole('searchbox', { name: 'Buscar en órdenes de compra' });
  fireEvent.change(contextual, { target: { value: 'OC-42' } });
  fireEvent.keyDown(contextual, { key: 'Enter' });
  const args = navigate.mock.calls[0][0];
  expect(args.to).toBe('/compras/ordenes');
  expect(args.search({ page: 5, estado: 1 })).toEqual({
    page: 1,
    estado: 1,
    offset: 0,
    q: 'OC-42',
  });
});
