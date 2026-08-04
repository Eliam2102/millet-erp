import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ConflictDialogProvider } from '@/components/erp/collaboration/ConflictDialogProvider';
import { NuevaRequisicionProvider } from '@/features/compras/components/NuevaRequisicionProvider';
import { useNuevaRequisicion } from '@/features/compras/components/nueva-requisicion-context';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    className,
  }: {
    children: React.ReactNode;
    to: string;
    className?: string;
  }) => (
    <a href={to} className={className}>
      {children}
    </a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
  useLocation: () => ({ pathname: '/compras/requisiciones', state: null }),
  useParams: () => ({}),
}));

function makeClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: 0, gcTime: 0 },
      mutations: { retry: false },
    },
  });
}

function Wrapper({ children }: { children: React.ReactNode }) {
  return (
    <QueryClientProvider client={makeClient()}>
      <ConflictDialogProvider>
        <NuevaRequisicionProvider>{children}</NuevaRequisicionProvider>
      </ConflictDialogProvider>
    </QueryClientProvider>
  );
}

function Abridor() {
  const { abrir } = useNuevaRequisicion();
  return (
    <button type="button" onClick={abrir}>
      abrir-sheet
    </button>
  );
}

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/catalogos/sucursales', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
    http.get('*/api/v1/catalogos/departamentos', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
    http.get('*/api/v1/catalogos/almacenes', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
    http.get('*/api/v1/catalogos/proveedores', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
    http.get('*/api/v1/identidad/usuarios', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'u@m.com', nombre: 'Test' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: ['compras.requisiciones.crear'],
    errorMessage: null,
  });
});

afterEach(() => {
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
    errorMessage: null,
  });
});

describe('<NuevaRequisicionProvider> — smoke', () => {
  it('cerrado por default: el form no está en el DOM', () => {
    render(
      <Wrapper>
        <Abridor />
      </Wrapper>,
    );
    expect(
      screen.queryByText(/llena la cabecera/i),
    ).not.toBeInTheDocument();
  });

  it('abrir() expone el form dentro del Sheet', async () => {
    render(
      <Wrapper>
        <Abridor />
      </Wrapper>,
    );
    fireEvent.click(screen.getByText('abrir-sheet'));
    expect(
      await screen.findByText(/llena la cabecera/i),
    ).toBeInTheDocument();
  });

  it('cerrar() sin dirty: cierra sin prompt', async () => {
    function CerrarTrigger() {
      const { abrir, cerrar } = useNuevaRequisicion();
      return (
        <div>
          <button type="button" onClick={abrir}>
            abrir
          </button>
          <button type="button" onClick={() => cerrar()}>
            cerrar
          </button>
        </div>
      );
    }
    const confirmSpy = vi.spyOn(window, 'confirm');
    render(
      <Wrapper>
        <CerrarTrigger />
      </Wrapper>,
    );
    fireEvent.click(screen.getByText('abrir'));
    expect(
      await screen.findByText(/llena la cabecera/i),
    ).toBeInTheDocument();
    fireEvent.click(screen.getByText('cerrar'));
    expect(confirmSpy).not.toHaveBeenCalled();
    confirmSpy.mockRestore();
  });

  it('cerrar() con dirty=true: muestra confirm; cancelar no cierra', async () => {
    function CerrarTrigger() {
      const { abrir, cerrar, setDirty } = useNuevaRequisicion();
      return (
        <div>
          <button type="button" onClick={abrir}>
            abrir
          </button>
          <button type="button" onClick={() => setDirty(true)}>
            ensuciar
          </button>
          <button type="button" onClick={() => cerrar()}>
            cerrar
          </button>
        </div>
      );
    }
    const confirmSpy = vi
      .spyOn(window, 'confirm')
      .mockReturnValueOnce(false);
    render(
      <Wrapper>
        <CerrarTrigger />
      </Wrapper>,
    );
    fireEvent.click(screen.getByText('abrir'));
    expect(
      await screen.findByText(/llena la cabecera/i),
    ).toBeInTheDocument();
    fireEvent.click(screen.getByText('ensuciar'));
    fireEvent.click(screen.getByText('cerrar'));
    expect(confirmSpy).toHaveBeenCalledTimes(1);
    // El sheet sigue abierto (usuario canceló).
    expect(screen.getByText(/llena la cabecera/i)).toBeInTheDocument();
    confirmSpy.mockRestore();
  });

  it('cerrar({ force: true }) bypasea el confirm aún con dirty', async () => {
    function CerrarTrigger() {
      const { abrir, cerrar, setDirty } = useNuevaRequisicion();
      return (
        <div>
          <button type="button" onClick={abrir}>
            abrir
          </button>
          <button type="button" onClick={() => setDirty(true)}>
            ensuciar
          </button>
          <button type="button" onClick={() => cerrar({ force: true })}>
            cerrar-force
          </button>
        </div>
      );
    }
    const confirmSpy = vi.spyOn(window, 'confirm');
    render(
      <Wrapper>
        <CerrarTrigger />
      </Wrapper>,
    );
    fireEvent.click(screen.getByText('abrir'));
    expect(
      await screen.findByText(/llena la cabecera/i),
    ).toBeInTheDocument();
    fireEvent.click(screen.getByText('ensuciar'));
    fireEvent.click(screen.getByText('cerrar-force'));
    expect(confirmSpy).not.toHaveBeenCalled();
    confirmSpy.mockRestore();
  });
});
