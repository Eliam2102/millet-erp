import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
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

  it('SÍ muestra UsuarioSelector cuando el usuario tiene seleccionar-requisitante', () => {
    useAuthStore.setState({
      ...useAuthStore.getState(),
      permisos: ['compras.requisiciones.seleccionar-requisitante'],
    });
    render(<NuevaRequisicion />, { wrapper: createQueryWrapper() });
    expect(
      screen.getByText(/requisitante \(delegación\)/i),
    ).toBeInTheDocument();
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
});
