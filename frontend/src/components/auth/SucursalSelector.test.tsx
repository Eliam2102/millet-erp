import { StrictMode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SucursalSelector } from '@/components/auth/SucursalSelector';
import { useAuthStore } from '@/lib/auth/auth-store';

/**
 * <c>&lt;SucursalSelector&gt;</c> — contexto operativo de sucursal
 * activa (01-05). Cubre los cuatro casos del alcance por sucursal
 * (B2): una sucursal se fija sola sin mezclar registros, varias
 * ofrecen selector manual, ninguna no renderiza nada, y una sucursal
 * ya guardada que dejó de estar permitida se limpia automáticamente.
 */
describe('<SucursalSelector>', () => {
  beforeEach(() => {
    mswServer.use(http.get('*/api/auth/alcance', () => HttpResponse.json({ esCorporativo: false })));
    useAuthStore.setState({
      status: 'authenticated',
      accessToken: 'test-token',
      expiresAt: new Date(Date.now() + 3600_000),
      user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
      empresas: [],
      currentEmpresaId: 'e-1',
      currentSucursalId: null,
      permisos: [],
      errorMessage: null,
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    useAuthStore.setState({
      status: 'idle',
      accessToken: null,
      expiresAt: null,
      user: null,
      empresas: [],
      currentEmpresaId: null,
      currentSucursalId: null,
      permisos: [],
      errorMessage: null,
    });
  });

  it('comparte una sola consulta durante el remontaje de StrictMode', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch');
    let solicitudes = 0;
    mswServer.use(http.get('*/api/auth/sucursales', () => {
      solicitudes += 1;
      return HttpResponse.json([{ id: 'suc-1', clave: 'CNK', nombre: 'Conkal' }]);
    }));
    const QueryWrapper = createQueryWrapper();
    render(<SucursalSelector />, { wrapper: ({ children }) => <StrictMode><QueryWrapper>{children}</QueryWrapper></StrictMode> });
    await waitFor(() => expect(useAuthStore.getState().currentSucursalId).toBe('suc-1'));
    expect(solicitudes).toBe(1);
    expect(fetchSpy.mock.calls.filter(([url]) => String(url).includes('/api/auth/sucursales'))).toHaveLength(1);
    expect(fetchSpy.mock.calls.filter(([url]) => String(url).includes('/api/auth/alcance'))).toHaveLength(1);
  });

  it('reutiliza las sucursales al remontar dentro de treinta segundos', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch');
    mswServer.use(http.get('*/api/auth/sucursales', () => HttpResponse.json([
      { id: 'suc-1', clave: 'CNK', nombre: 'Conkal' },
    ])));
    const wrapper = createQueryWrapper();
    const first = render(<SucursalSelector />, { wrapper });
    await waitFor(() => expect(useAuthStore.getState().currentSucursalId).toBe('suc-1'));
    first.unmount();
    render(<SucursalSelector />, { wrapper });
    await screen.findByRole('option', { name: 'CNK · Conkal' });
    expect(fetchSpy.mock.calls.filter(([url]) => String(url).includes('/api/auth/sucursales'))).toHaveLength(1);
    expect(fetchSpy.mock.calls.filter(([url]) => String(url).includes('/api/auth/alcance'))).toHaveLength(1);
  });

  it('una sola sucursal permitida: se fija sola sin mezclar otros registros', async () => {
    mswServer.use(
      http.get('*/api/auth/sucursales', () =>
        HttpResponse.json([{ id: 'suc-1', clave: 'MTY', nombre: 'Monterrey' }]),
      ),
    );

    render(<SucursalSelector />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(useAuthStore.getState().currentSucursalId).toBe('suc-1'),
    );

    const select = screen.getByLabelText('Sucursal activa') as HTMLSelectElement;
    expect(select.value).toBe('suc-1');
    // Solo la propia sucursal — no se agregó ni mezcló ninguna otra.
    expect(screen.getAllByRole('option')).toHaveLength(2); // "Todas" + MTY
  });

  it('corporativo no se adscribe automáticamente aunque sólo exista una sucursal', async () => {
    mswServer.use(
      http.get('*/api/auth/alcance', () => HttpResponse.json({ esCorporativo: true })),
      http.get('*/api/auth/sucursales', () => HttpResponse.json([{ id: 'suc-1', clave: 'MTY', nombre: 'Monterrey' }])),
    );
    render(<SucursalSelector />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Corporativo · Millet:')).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Todas las sucursales' })).toBeInTheDocument();
    expect(useAuthStore.getState().currentSucursalId).toBeNull();
  });

  it('varias sucursales permitidas: ofrece selector manual sin auto-elegir', async () => {
    mswServer.use(
      http.get('*/api/auth/sucursales', () =>
        HttpResponse.json([
          { id: 'suc-1', clave: 'MTY', nombre: 'Monterrey' },
          { id: 'suc-2', clave: 'CDMX', nombre: 'Ciudad de México' },
        ]),
      ),
    );

    render(<SucursalSelector />, { wrapper: createQueryWrapper() });

    const select = await screen.findByLabelText('Sucursal activa') as HTMLSelectElement;
    await waitFor(() => expect(select).not.toBeDisabled());
    // No se auto-selecciona con más de una opción disponible.
    expect(useAuthStore.getState().currentSucursalId).toBeNull();
    expect(select.value).toBe('');
    expect(screen.getAllByRole('option')).toHaveLength(3); // "Todas" + 2

    fireEvent.change(select, { target: { value: 'suc-2' } });
    expect(useAuthStore.getState().currentSucursalId).toBe('suc-2');
  });

  it('sin sucursales permitidas: no renderiza el selector', async () => {
    mswServer.use(
      http.get('*/api/auth/sucursales', () => HttpResponse.json([])),
    );

    const { container } = render(<SucursalSelector />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() =>
      expect(screen.queryByText('Sucursal:')).not.toBeInTheDocument(),
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('sucursal guardada que ya no está permitida: se limpia', async () => {
    // Dos sucursales permitidas (no una sola) para que el efecto de
    // "se limpia" no se confunda con el de "una sola se fija sola".
    useAuthStore.setState({ currentSucursalId: 'suc-obsoleta' });
    mswServer.use(
      http.get('*/api/auth/sucursales', () =>
        HttpResponse.json([
          { id: 'suc-1', clave: 'MTY', nombre: 'Monterrey' },
          { id: 'suc-2', clave: 'CDMX', nombre: 'Ciudad de México' },
        ]),
      ),
    );

    render(<SucursalSelector />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(useAuthStore.getState().currentSucursalId).toBeNull(),
    );
    const select = screen.getByLabelText('Sucursal activa') as HTMLSelectElement;
    expect(select.value).toBe('');
  });
});
