import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import { AppShell } from './AppShell';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
vi.mock('@tanstack/react-router', () => ({
  useLocation: () => ({ pathname: '/compras/requisiciones/nueva' }),
  useNavigate: () => vi.fn(),
  Link: ({ to, children, ...props }: { to: string; children: ReactNode }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
}));
vi.mock('@/lib/auth/useAuth', () => ({
  useAuth: () => ({ isAuthenticated: true, isSwitchingEmpresa: false }),
}));
vi.mock('./Topbar', () => ({
  Topbar: ({
    desktopNavigation,
    onMenuClick,
  }: {
    desktopNavigation: ReactNode;
    onMenuClick: () => void;
  }) => (
    <header>
      <button onClick={onMenuClick}>Abrir navegación</button>
      {desktopNavigation}
    </header>
  ),
}));
vi.mock('./AppLauncherModal', () => ({ AppLauncherModal: () => null }));
vi.mock('@/features/integraciones-fiscal/components/AvisoCsdBanner', () => ({
  AvisoCsdBanner: () => null,
}));
beforeEach(() =>
  useAuthStore.setState({ permisos: [P.ComprasRequisicionesLeer, P.ComprasOrdenesLeer] }),
);
function mount() {
  render(
    <AppShell>
      <input aria-label="Dato capturado" defaultValue="Se conserva" />
    </AppShell>,
  );
}
it('inicia oculto y cierra al salir, conservando el formulario montado', async () => {
  mount();
  expect(screen.queryByRole('dialog', { name: 'Menú de navegación' })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Abrir menú' }));
  const menu = await screen.findByRole('dialog', { name: 'Menú de navegación' });
  fireEvent.mouseLeave(menu);
  await waitFor(() =>
    expect(screen.queryByRole('dialog', { name: 'Menú de navegación' })).not.toBeInTheDocument(),
  );
  expect(screen.getByLabelText('Dato capturado')).toHaveValue('Se conserva');
});
it('permite fijar sin ocultar al salir y Escape libera la pantalla', async () => {
  mount();
  fireEvent.click(screen.getByRole('button', { name: 'Abrir menú' }));
  const menu = await screen.findByRole('dialog', { name: 'Menú de navegación' });
  fireEvent.click(screen.getByRole('button', { name: 'Fijar navegación' }));
  fireEvent.mouseLeave(menu);
  expect(menu).toBeInTheDocument();
  expect(screen.getByRole('main').parentElement).toHaveAttribute('data-navigation-pinned', 'true');
  fireEvent.keyDown(menu, { key: 'Escape' });
  await waitFor(() =>
    expect(screen.queryByRole('dialog', { name: 'Menú de navegación' })).not.toBeInTheDocument(),
  );
  expect(screen.getByRole('main').parentElement).toHaveAttribute('data-navigation-pinned', 'false');
});
it('no interrumpe la búsqueda enfocada al salir con el puntero', async () => {
  mount();
  fireEvent.click(screen.getByRole('button', { name: 'Abrir menú' }));
  const menu = await screen.findByRole('dialog', { name: 'Menú de navegación' });
  screen.getByLabelText('Filtrar menú del módulo').focus();
  fireEvent.mouseLeave(menu);
  expect(menu).toBeInTheDocument();
});

it('elegir una pantalla cierra el menú sin desmontar la captura', async () => {
  mount();
  fireEvent.click(screen.getByRole('button', { name: 'Abrir menú' }));
  await screen.findByRole('dialog', { name: 'Menú de navegación' });
  fireEvent.click(screen.getByRole('link', { name: 'Mis requisiciones' }));
  await waitFor(() =>
    expect(screen.queryByRole('dialog', { name: 'Menú de navegación' })).not.toBeInTheDocument(),
  );
  expect(screen.getByLabelText('Dato capturado')).toHaveValue('Se conserva');
});
it('con el menú fijado permite enfocar y editar el formulario', async () => {
  mount();
  fireEvent.click(screen.getByRole('button', { name: 'Abrir menú' }));
  const menu = await screen.findByRole('dialog', { name: 'Menú de navegación' });
  fireEvent.click(screen.getByRole('button', { name: 'Fijar navegación' }));
  const input = screen.getByLabelText('Dato capturado');
  input.focus();
  fireEvent.pointerDown(input, { button: 0 });
  fireEvent.change(input, { target: { value: 'Editado con menú fijo' } });
  expect(input).toHaveFocus();
  expect(input).toHaveValue('Editado con menú fijo');
  expect(menu).toBeInTheDocument();
});

it('ancla el panel al borde izquierdo y anima entrada y salida laterales', async () => {
  mount();
  fireEvent.click(screen.getByRole('button', { name: 'Abrir menú' }));
  const menu = await screen.findByRole('dialog', { name: 'Menú de navegación' });
  expect(menu).toHaveClass('fixed', 'left-0', 'top-topbar', 'bottom-0');
  expect(menu).toHaveClass('data-[state=open]:slide-in-from-left');
  expect(menu).toHaveClass('data-[state=closed]:slide-out-to-left');
  expect(menu).toHaveClass('motion-reduce:animate-none');
});

it('abrir y cerrar el menú móvil conserva texto, selección y surtido capturados', async () => {
  render(
    <AppShell>
      <input aria-label="Descripción capturada" defaultValue="" />
      <select aria-label="Prioridad capturada" defaultValue="normal">
        <option value="normal">Normal</option>
        <option value="alta">Alta</option>
      </select>
      <label>
        <input type="checkbox" />
        Surtido completo
      </label>
    </AppShell>,
  );
  const descripcion = screen.getByLabelText('Descripción capturada');
  const prioridad = screen.getByLabelText('Prioridad capturada');
  const surtido = screen.getByLabelText('Surtido completo');
  fireEvent.change(descripcion, { target: { value: 'Captura móvil sin guardar' } });
  fireEvent.change(prioridad, { target: { value: 'alta' } });
  fireEvent.click(surtido);
  for (const cierre of ['botón', 'Escape']) {
    fireEvent.click(screen.getByRole('button', { name: 'Abrir navegación' }));
    const menu = await screen.findByRole('dialog', { name: 'Navegación principal' });
    if (cierre === 'botón')
      fireEvent.click(screen.getByRole('button', { name: 'Cerrar navegación' }));
    else fireEvent.keyDown(menu, { key: 'Escape' });
    await waitFor(() =>
      expect(
        screen.queryByRole('dialog', { name: 'Navegación principal' }),
      ).not.toBeInTheDocument(),
    );
    expect(screen.getByLabelText('Descripción capturada')).toBe(descripcion);
    expect(descripcion).toHaveValue('Captura móvil sin guardar');
    expect(prioridad).toHaveValue('alta');
    expect(surtido).toBeChecked();
  }
});

it('abre automáticamente al acercar el cursor al borde izquierdo sin perder captura', async () => {
  mount();
  const input = screen.getByLabelText('Dato capturado');
  fireEvent.change(input, { target: { value: 'Sin guardar' } });
  fireEvent.mouseEnter(screen.getByTestId('navigation-edge'));
  const menu = await screen.findByRole('dialog', { name: 'Menú de navegación' });
  fireEvent.mouseLeave(menu);
  await waitFor(() =>
    expect(screen.queryByRole('dialog', { name: 'Menú de navegación' })).not.toBeInTheDocument(),
  );
  expect(input).toHaveValue('Sin guardar');
});

it('cierra si el cursor roza el borde y sale antes de entrar al panel', async () => {
  mount();
  const edge = screen.getByTestId('navigation-edge');
  fireEvent.mouseEnter(edge);
  await screen.findByRole('dialog', { name: 'Menú de navegación' });
  fireEvent.mouseLeave(edge, { relatedTarget: screen.getByRole('main') });
  await waitFor(() =>
    expect(screen.queryByRole('dialog', { name: 'Menú de navegación' })).not.toBeInTheDocument(),
  );
});

it('no añade la marca Millet ERP junto al botón de navegación de escritorio', () => {
  mount();
  expect(screen.queryByText('Millet ERP')).not.toBeInTheDocument();
});
