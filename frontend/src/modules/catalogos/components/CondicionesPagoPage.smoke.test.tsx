import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { CondicionesPagoPage } from '@/modules/catalogos/components/CondicionesPagoPage';
import { NuevaCondicionesPagoProvider } from '@/modules/catalogos/components/SheetNuevaCondicionesPago';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke del Grupo 2 (catálogo editable) — usamos CondicionesPago como
 * exemplar. Valida que la tabla renderiza filas + el botón "Nueva
 * condición" cuando hay permiso.
 */
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    ...rest
  }: {
    children: React.ReactNode;
    to: string;
    [key: string]: unknown;
  }) => (
    <a href={to} {...rest}>
      {children}
    </a>
  ),
  useNavigate: () => () => undefined,
}));

const ITEMS = [
  {
    id: 'c-1',
    clave: '30D',
    nombre: '30 días',
    diasCredito: 30,
    estatus: 0,
    version: 1,
  },
  {
    id: 'c-2',
    clave: '60D',
    nombre: '60 días',
    diasCredito: 60,
    estatus: 0,
    version: 1,
  },
];

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [
      PermisosCanonicos.CompartidoCatalogosLeer,
      PermisosCanonicos.CatalogosCondicionesPagoGestionar,
    ],
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

describe('<CondicionesPagoPage> — smoke', () => {
  it('renderiza tabla con filas y botón "Nueva condición"', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/condiciones-pago', () =>
        HttpResponse.json(ITEMS),
      ),
    );

    render(
      <NuevaCondicionesPagoProvider>
        <CondicionesPagoPage />
      </NuevaCondicionesPagoProvider>,
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() =>
      expect(screen.getByText('30 días')).toBeInTheDocument(),
    );

    expect(screen.getByText('60 días')).toBeInTheDocument();
    expect(screen.getAllByText('30D')[0]).toBeInTheDocument();
    expect(
      screen.getAllByRole('button', { name: /nueva condición/i })[0],
    ).toBeInTheDocument();
  });
});
