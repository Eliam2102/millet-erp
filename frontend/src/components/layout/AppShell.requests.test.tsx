import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { type ComponentProps, type ReactNode } from 'react';
import { beforeEach, expect, it, vi } from 'vitest';
import { AppShell } from './AppShell';
import { AdminLandingPage } from '@/components/admin/AdminLandingPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { apiRequest } from '@/lib/api/client';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ to, children, ...props }: ComponentProps<'a'> & { to: string }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
  useNavigate: () => vi.fn(),
  useLocation: () => ({ pathname: '/admin' }),
}));
vi.mock('@/lib/auth/useAuth', () => ({
  useAuth: () => ({
    isAuthenticated: true,
    isSwitchingEmpresa: false,
    user: useAuthStore.getState().user,
    currentEmpresa: { razonSocial: 'Empresa de prueba' },
    empresas: [],
    logout: vi.fn(),
  }),
}));
vi.mock('@/components/layout/Topbar', () => ({ Topbar: () => <header>Ubicación</header> }));
vi.mock('@/components/layout/AccessSearch', () => ({
  AccessSearch: () => <button>Buscar</button>,
}));
vi.mock('@/lib/api/client', () => ({ apiRequest: vi.fn() }));

beforeEach(() => {
  localStorage.clear();
  vi.mocked(apiRequest).mockReset();
  useAuthStore.setState({
    status: 'authenticated',
    user: { id: 'persona-prueba', nombre: 'Persona de prueba', email: 'prueba@example.test' },
    currentEmpresaId: 'empresa-prueba',
    currentSucursalId: null,
    permisos: Object.values(P),
  });
});

it.each([false, true])(
  'el menú y Administración no reinician consultas al renderizar (StrictMode=%s)',
  async (strict) => {
    const solicitudes: { path: string; signal: AbortSignal; resolver: () => void }[] = [];
    vi.mocked(apiRequest).mockImplementation(
      (path, options) =>
        new Promise((resolve, reject) => {
          const signal = options?.signal as AbortSignal;
          signal.addEventListener('abort', () =>
            reject(new DOMException('Cancelada', 'AbortError')),
          );
          solicitudes.push({
            path,
            signal,
            resolver: () =>
              resolve({
                status: 200,
                data:
                  path === '/api/auth/sucursales'
                    ? [{ id: 'sucursal-prueba', nombre: 'Sucursal de prueba', clave: 'PRUEBA' }]
                    : path.includes('/equivalencias/')
                      ? []
                      : {
                          items: [],
                          total: 0,
                          totalCount: 0,
                          countPartidasAbiertas: 0,
                          countAtrasadas: 0,
                        },
              }),
          });
        }),
    );
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false, staleTime: 30_000 } },
    });
    function Pantalla({ children }: { children?: ReactNode }) {
      return (
        <QueryClientProvider client={client}>
          <AppShell>
            <AdminLandingPage />
            {children}
          </AppShell>
        </QueryClientProvider>
      );
    }
    const view = render(<Pantalla />, { reactStrictMode: strict });
    try {
      await waitFor(() => expect(solicitudes).toHaveLength(13));
      for (let i = 0; i < 3; i++)
        view.rerender(
          <Pantalla>
            <p>Render {i}</p>
          </Pantalla>,
        );
      // Respuestas escalonadas: el primer resultado provoca renders mientras las demás siguen en vuelo.
      await act(async () =>
        solicitudes.filter((s) => s.path === '/api/auth/sucursales').forEach((s) => s.resolver()),
      );
      await waitFor(() => expect(solicitudes).toHaveLength(15));
      await act(async () =>
        solicitudes.filter((s) => s.path !== '/api/auth/sucursales').forEach((s) => s.resolver()),
      );
      await waitFor(() => expect(client.isFetching()).toBe(0));
      fireEvent.change(screen.getByRole('textbox', { name: 'Filtrar menú' }), {
        target: { value: 'facturas' },
      });
      fireEvent.change(screen.getByRole('searchbox', { name: 'Buscar una configuración' }), {
        target: { value: 'folios' },
      });
      for (let i = 0; i < 3; i++)
        view.rerender(
          <Pantalla>
            <p>Render final {i}</p>
          </Pantalla>,
        );
      expect(new Set(solicitudes.map((s) => s.path)).size).toBe(15);
      expect(solicitudes).toHaveLength(15);
      expect(solicitudes.every((s) => !s.signal.aborted)).toBe(true);
      fireEvent.click(screen.getByRole('button', { name: 'Contraer menú' }));
      fireEvent.click(screen.getByRole('button', { name: 'Expandir menú' }));
      fireEvent.click(screen.getByRole('button', { name: 'Módulos' }));
      fireEvent.click(screen.getByRole('button', { name: 'Cerrar navegación' }));
      await act(async () => {});
      expect(solicitudes).toHaveLength(15);

      // La corrección no bloquea el refresco habitual por invalidación.
      let refresco: Promise<void>;
      act(() => {
        refresco = client.invalidateQueries({ queryKey: ['admin', 'pendientes', 'parametros'] });
      });
      await waitFor(() => expect(solicitudes).toHaveLength(16));
      expect(solicitudes[15].path).toBe('/api/v1/admin/parametros?modulo=');
      await act(async () => {
        solicitudes[15].resolver();
        await refresco;
      });
      expect(client.isFetching()).toBe(0);

      // Una cancelación explícita después del arranque sí aborta el HTTP.
      act(() => {
        void client.invalidateQueries({ queryKey: ['admin', 'pendientes', 'parametros'] });
      });
      await waitFor(() => expect(solicitudes).toHaveLength(17));
      await act(async () => {
        await client.cancelQueries({ queryKey: ['admin', 'pendientes', 'parametros'] });
      });
      expect(solicitudes[16].signal.aborted).toBe(true);
      view.rerender(
        <Pantalla>
          <p>Después de cancelar</p>
        </Pantalla>,
      );
      await act(async () => {});
      expect(solicitudes).toHaveLength(17);
    } finally {
      view.unmount();
      client.clear();
    }
  },
);
