import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { AppShell } from './AppShell';
import { useAuthStore } from '@/lib/auth/auth-store';
vi.mock('@tanstack/react-router', () => ({
  Link: ({ to, children }: { to: string; children: React.ReactNode }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => vi.fn(),
  useLocation: () => ({ pathname: '/' }),
}));
vi.mock('@/lib/auth/useAuth', () => ({
  useAuth: () => ({ isAuthenticated: true, isSwitchingEmpresa: false, logout: vi.fn() }),
}));
vi.mock('@/features/inicio/api/useResumenInicio', () => ({
  useResumenInicio: () => ({ filas: [], errores: [], cargando: false }),
}));
vi.mock('@/components/layout/AccessSearch', () => ({
  AccessSearch: () => <button>Buscar</button>,
}));
vi.mock('@/components/layout/Topbar', () => ({ Topbar: () => <header>Ubicación actual</header> }));
vi.mock('@/components/auth/EmpresaSelector', () => ({ EmpresaSelector: () => null }));
vi.mock('@/components/auth/SucursalSelector', () => ({ SucursalSelector: () => null }));
beforeEach(() => {
  localStorage.clear();
  useAuthStore.setState({ permisos: [] });
});
it('contrae, expande y recuerda la preferencia al volver a montar; no hay segundo panel', () => {
  const first = render(
    <AppShell>
      <p>Pantalla</p>
    </AppShell>,
  );
  fireEvent.click(screen.getByRole('button', { name: 'Contraer menú' }));
  expect(localStorage.getItem('millet.menu.collapsed')).toBe('true');
  expect(first.container.querySelector('.w-menu-collapsed')).toBeInTheDocument();
  first.unmount();
  const second = render(
    <AppShell>
      <p>Pantalla</p>
    </AppShell>,
  );
  expect(screen.getByRole('button', { name: 'Expandir menú' })).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Expandir menú' }));
  expect(localStorage.getItem('millet.menu.collapsed')).toBe('false');
  expect(second.container.querySelector('.w-module-panel')).toBeNull();
  expect(screen.getByRole('navigation', { name: 'Navegación móvil' })).toHaveTextContent(
    'InicioPendientesBuscarMódulos',
  );
});
it('sigue funcionando cuando localStorage rechaza lectura y escritura', () => {
  const get = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
    throw new Error('sin almacenamiento');
  });
  const set = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
    throw new Error('sin almacenamiento');
  });
  render(
    <AppShell>
      <p>Pantalla</p>
    </AppShell>,
  );
  fireEvent.click(screen.getByRole('button', { name: 'Contraer menú' }));
  expect(screen.getByRole('button', { name: 'Expandir menú' })).toBeInTheDocument();
  get.mockRestore();
  set.mockRestore();
});
