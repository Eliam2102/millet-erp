import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { NuevaRequisicion } from '@/features/compras/pages/NuevaRequisicion';
import { useAuthStore } from '@/lib/auth/auth-store';
import { buildNuevaRequisicionDraftKey } from '@/features/compras/lib/draft-storage';

// Mock TanStack Router pieces que la página consume. No queremos
// montar un router completo para un smoke test; solo verificar que
// renderiza el form, breadcrumbs y modal cuando aplica.
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
}));

function setupCatalogos() {
  // Endpoints que la página dispara al montar (selectores org cargan
  // su catálogo eager).
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
    http.get('*/api/v1/identidad/usuarios', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
  );
}

beforeEach(() => {
  setupCatalogos();
  // Setup de auth-store mínimo (user + empresa) para que la draft key
  // se construya y el form sepa el contexto.
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: [], // sin seleccionar-requisitante
    errorMessage: null,
  });
  window.localStorage.clear();
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
  window.localStorage.clear();
});

describe('<NuevaRequisicion> — smoke', () => {
  it('renderiza el form con título y botón Crear requisición', () => {
    render(<NuevaRequisicion />, { wrapper: createQueryWrapper() });
    // design/frontend-polish: ya no hay breadcrumbs.
    expect(screen.getByText('Nueva requisición')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /crear requisición/i }),
    ).toBeInTheDocument();
  });

  it('NO muestra UsuarioSelector cuando el usuario carece de seleccionar-requisitante', () => {
    render(<NuevaRequisicion />, { wrapper: createQueryWrapper() });
    expect(
      screen.queryByText(/requisitante \(delegación\)/i),
    ).not.toBeInTheDocument();
  });

  it('SÍ muestra UsuarioSelector cuando puede delegar y leer usuarios', () => {
    useAuthStore.setState({
      ...useAuthStore.getState(),
      permisos: ['compras.requisiciones.seleccionar-requisitante', 'identidad.usuarios.leer'],
    });
    render(<NuevaRequisicion />, { wrapper: createQueryWrapper() });
    expect(
      screen.getByText(/requisitante \(delegación\)/i),
    ).toBeInTheDocument();
  });

  it('NO muestra delegación cuando puede delegar pero no leer usuarios', () => {
    useAuthStore.setState({ permisos: ['compras.requisiciones.seleccionar-requisitante'] });
    render(<NuevaRequisicion />, { wrapper: createQueryWrapper() });
    expect(screen.queryByText(/requisitante \(delegación\)/i)).not.toBeInTheDocument();
  });

  it('muestra modal "Recuperar borrador" cuando hay draft persistido', () => {
    const key = buildNuevaRequisicionDraftKey('u-test', 'e-test')!;
    window.localStorage.setItem(
      key,
      JSON.stringify({
        values: {
          sucursalId: '',
          departamentoId: '',
          clasificacion: 0,
          prioridad: 1,
          fechaSolicitud: new Date().toISOString(),
        },
        meta: { savedAt: '2026-05-09T10:00:00Z' },
      }),
    );

    render(<NuevaRequisicion />, { wrapper: createQueryWrapper() });
    expect(
      screen.getByText('Tienes un borrador guardado'),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /recuperar borrador/i }),
    ).toBeInTheDocument();
  });

  it('Esc en "Recuperar borrador" solo cierra: el borrador se conserva', () => {
    const key = buildNuevaRequisicionDraftKey('u-test', 'e-test')!;
    window.localStorage.setItem(
      key,
      JSON.stringify({
        values: { sucursalId: '', departamentoId: '', clasificacion: 0, prioridad: 1, fechaSolicitud: new Date().toISOString() },
        meta: { savedAt: '2026-05-09T10:00:00Z' },
      }),
    );
    render(<NuevaRequisicion />, { wrapper: createQueryWrapper() });
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' });
    expect(screen.queryByText('Tienes un borrador guardado')).not.toBeInTheDocument();
    expect(window.localStorage.getItem(key)).not.toBeNull();
  });

  it('los campos tienen label asociado (getByLabelText)', () => {
    render(<NuevaRequisicion />, { wrapper: createQueryWrapper() });
    for (const nombre of [/^Sucursal/, /^Departamento/, /^Clasificación/, /^Prioridad/, /^Proveedor sugerido/, /^Descripción/]) {
      expect(screen.getByLabelText(nombre)).toBeInTheDocument();
    }
  });
});

it('permite elegir surtido completo o parcial por partida', () => {
  render(<NuevaRequisicion />, { wrapper: createQueryWrapper() });
  const completo = screen.getByRole('checkbox', { name: /surtir partidas completas/i });
  expect(completo).not.toBeChecked();
  fireEvent.click(completo);
  expect(completo).toBeChecked();
});
