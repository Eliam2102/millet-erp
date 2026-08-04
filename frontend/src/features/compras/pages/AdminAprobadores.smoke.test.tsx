import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { AdminAprobadores } from '@/features/compras/pages/AdminAprobadores';
import { useAuthStore } from '@/lib/auth/auth-store';
import { RolAprobador } from '@/features/compras/api/types';

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
  useSearch: () => ({ tab: 'vigentes' as const }),
}));

const DEPTO_ID = '11111111-1111-4111-8111-111111111111';
const USER_ID = '22222222-2222-4222-8222-222222222222';
const APROBADOR_ID = '33333333-3333-4333-8333-333333333333';

function setupCatalogos() {
  mswServer.use(
    http.get('*/api/v1/catalogos/departamentos', () =>
      HttpResponse.json({
        items: [{ id: DEPTO_ID, nombre: 'Mantenimiento', clave: 'MTO' }],
        offset: 0,
        limit: 200,
        total: 1,
      }),
    ),
    http.get('*/api/v1/identidad/usuarios', () =>
      HttpResponse.json({
        items: [{ id: USER_ID, nombre: 'Pedro García', email: 'p@m.com' }],
        offset: 0,
        limit: 200,
        total: 1,
      }),
    ),
  );
}

beforeEach(() => {
  setupCatalogos();
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: USER_ID, email: 'p@m.com', nombre: 'Pedro García' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: ['compras.aprobadores.administrar'],
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

describe('<AdminAprobadores> — smoke', () => {
  it('estado vigentes vacío: muestra EmptyState con CTA "Designar"', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/aprobadores', () =>
        HttpResponse.json([]),
      ),
    );
    render(<AdminAprobadores />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/sin aprobadores vigentes/i),
      ).toBeInTheDocument(),
    );
    expect(
      screen.getByRole('button', { name: /designar aprobador/i }),
    ).toBeInTheDocument();
  });

  it('vigentes con datos: tabla muestra depto + rol resueltos', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/aprobadores', () =>
        HttpResponse.json([
          {
            id: APROBADOR_ID,
            departamentoId: DEPTO_ID,
            rol: RolAprobador.JefeDpto,
            usuarioId: USER_ID,
            vigenteDesde: '2026-05-09T10:00:00Z',
            designadoPor: USER_ID,
            motivo: null,
          },
        ]),
      ),
    );
    render(<AdminAprobadores />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText('Jefe de departamento'),
      ).toBeInTheDocument(),
    );
    expect(screen.getByText('Mantenimiento')).toBeInTheDocument();
    expect(screen.getByText('Pedro García')).toBeInTheDocument();
    // Botón Revocar de la fila.
    expect(
      screen.getByRole('button', { name: /revocar aprobador/i }),
    ).toBeInTheDocument();
  });

  it('renderiza tabs Vigentes / Histórico como tablist', () => {
    mswServer.use(
      http.get('*/api/v1/compras/aprobadores', () =>
        HttpResponse.json([]),
      ),
    );
    render(<AdminAprobadores />, { wrapper: createQueryWrapper() });
    expect(
      screen.getByRole('tablist', { name: /vigentes o histórico/i }),
    ).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /vigentes/i })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(
      screen.getByRole('tab', { name: /histórico/i }),
    ).toHaveAttribute('aria-selected', 'false');
  });

  it('estado error: muestra ErrorState', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/aprobadores', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Error de servidor',
            status: 500,
          },
          {
            status: 500,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );
    render(<AdminAprobadores />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/no se pudieron cargar los aprobadores/i),
      ).toBeInTheDocument(),
    );
  });
});
