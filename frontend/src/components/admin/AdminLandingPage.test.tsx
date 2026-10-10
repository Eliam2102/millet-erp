import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import type { ComponentProps } from 'react';
import { AdminLandingPage } from './AdminLandingPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { gruposAdmin } from '@/lib/admin/registry';
vi.mock('@tanstack/react-router', () => ({
  Link: ({ to, children, ...props }: ComponentProps<'a'> & { to: string }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
}));
vi.mock('@/lib/admin/use-admin-pendientes', () => ({
  useAdminPendientes: () => ({
    pendientes: [],
    cargando: false,
    error: false,
    requiereSucursal: false,
  }),
}));
beforeEach(() => useAuthStore.setState({ permisos: Object.values(P) }));
it('todas las secciones visibles, en orden, con índice por anclas y catálogos SAT aparte', () => {
  const { container } = render(<AdminLandingPage />);
  expect(
    screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent),
  ).toEqual(['Falta configurar o validar', ...gruposAdmin.map((group) => group.titulo)]);
  expect(screen.getByText('Del SAT · solo consulta')).toBeInTheDocument();
  const index = screen.getByRole('navigation', { name: 'Secciones de Administración' });
  expect(within(index).getByRole('link', { name: 'Personas y accesos' })).toHaveAttribute(
    'href',
    '#admin-identidad',
  );
  expect(container.querySelector('details, summary, [role="tab"], [role="tabpanel"]')).toBeNull();
});
it('busca filas sin acentos y omite secciones sin coincidencias', () => {
  render(<AdminLandingPage />);
  fireEvent.change(screen.getByRole('searchbox', { name: 'Buscar una configuración' }), {
    target: { value: 'folios' },
  });
  expect(screen.getByRole('link', { name: /Series y folios/ })).toBeInTheDocument();
  expect(screen.queryByRole('link', { name: /Mi empresa/ })).not.toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Personas y accesos' })).not.toBeInTheDocument();
});
it('sin permiso no hay filas ni datos maestros en Administración', () => {
  useAuthStore.setState({
    permisos: [P.IdentidadUsuariosLeer, P.DatosMaestrosProveedoresGestionar],
  });
  render(<AdminLandingPage />);
  expect(screen.getByRole('link', { name: /Cuentas de acceso/ })).toBeInTheDocument();
  for (const title of [
    'Mi empresa',
    'Roles y permisos',
    'Proveedores',
    'Artículos',
    'Clientes',
    'Productos A+W',
  ])
    expect(screen.queryByRole('link', { name: new RegExp(title) })).not.toBeInTheDocument();
});
it('por módulo conserva configuración permitida por cualquiera de sus permisos', () => {
  useAuthStore.setState({ permisos: [P.CentrosCostoCatalogoAdministrar] });
  render(<AdminLandingPage />);
  expect(screen.getByRole('link', { name: /Centros de costo/ })).toHaveAttribute(
    'href',
    '/centros-costo/configuracion',
  );
  expect(screen.queryByRole('link', { name: /Asignación de centros/ })).not.toBeInTheDocument();
});

it('el índice indica la sección que el observador reporta como visible', () => {
  let notify: IntersectionObserverCallback | undefined;
  vi.stubGlobal(
    'IntersectionObserver',
    class {
      constructor(callback: IntersectionObserverCallback) {
        notify = callback;
      }
      observe() {}
      disconnect() {}
    },
  );
  render(<AdminLandingPage />);
  const entries: IntersectionObserverEntry[] = ['organizacion', 'identidad'].map((id, index) => ({
    target: document.getElementById(`admin-${id}`)!,
    isIntersecting: index === 1,
    boundingClientRect: new DOMRect(0, index === 0 ? -400 : 100, 600, 300),
    intersectionRatio: index === 1 ? 1 : 0,
    intersectionRect: new DOMRect(),
    rootBounds: null,
    time: 0,
  }));
  act(() => notify?.(entries, {} as IntersectionObserver));
  const index = within(screen.getByRole('navigation', { name: 'Secciones de Administración' }));
  expect(index.getByRole('link', { name: 'Personas y accesos' })).toHaveAttribute(
    'aria-current',
    'location',
  );
  expect(index.getByRole('link', { name: 'Empresa y sucursales' })).not.toHaveAttribute(
    'aria-current',
  );
  vi.unstubAllGlobals();
});
