import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { MonedaDetalle } from '@/modules/catalogos/components/MonedaDetalle';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke del detalle de moneda (P3 del patrón cross-módulo). Mockea
 * router (useParams + Link) y usa MSW para los GET de la lista de
 * monedas + el histórico de tipos de cambio.
 */
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    className,
    ...rest
  }: {
    children: React.ReactNode;
    to: string;
    className?: string;
    [key: string]: unknown;
  }) => (
    <a href={to} className={className} {...rest}>
      {children}
    </a>
  ),
  useParams: () => ({ id: 'm-1' }),
}));

const MONEDAS = [
  {
    id: 'm-1',
    codigo: 'MXN',
    nombre: 'Peso mexicano',
    decimales: 2,
    activa: true,
    version: 1,
  },
  {
    id: 'm-2',
    codigo: 'USD',
    nombre: 'Dólar americano',
    decimales: 4,
    activa: true,
    version: 1,
  },
];

const TIPOS_CAMBIO_VACIO = {
  items: [],
  offset: 0,
  limit: 50,
  total: 0,
};

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
      PermisosCanonicos.CatalogosMonedasGestionar,
      PermisosCanonicos.CatalogosTiposCambioGestionar,
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

describe('<MonedaDetalle> — smoke', () => {
  it('renderiza header con código + badge Activa, los 2 tabs y el form de datos', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/monedas', () => HttpResponse.json(MONEDAS)),
      http.get('*/api/v1/catalogos/monedas/m-1/tipos-cambio', () =>
        HttpResponse.json(TIPOS_CAMBIO_VACIO),
      ),
    );

    render(<MonedaDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText('MXN')).toBeInTheDocument());

    // Header: código + al menos un badge "Activa" (puede aparecer en
    // header + label del switch del form, ambos válidos).
    expect(screen.getAllByText('Activa').length).toBeGreaterThan(0);

    // 2 tabs.
    expect(screen.getByRole('tab', { name: /^datos$/i })).toBeInTheDocument();
    expect(
      screen.getByRole('tab', { name: /histórico tipos de cambio/i }),
    ).toBeInTheDocument();

    // Form de datos: input con el nombre.
    expect(
      screen.getByDisplayValue('Peso mexicano'),
    ).toBeInTheDocument();

    // Acción "Desactivar" disponible (moneda activa).
    expect(
      screen.getByRole('button', { name: /^desactivar$/i }),
    ).toBeInTheDocument();
  });

  it('muestra "Reactivar" cuando la moneda está inactiva', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/monedas', () =>
        HttpResponse.json([{ ...MONEDAS[0], activa: false }]),
      ),
      http.get('*/api/v1/catalogos/monedas/m-1/tipos-cambio', () =>
        HttpResponse.json(TIPOS_CAMBIO_VACIO),
      ),
    );

    render(<MonedaDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText('MXN')).toBeInTheDocument());

    expect(screen.getAllByText('Inactiva').length).toBeGreaterThan(0);
    expect(
      screen.getByRole('button', { name: /^reactivar$/i }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /^desactivar$/i }),
    ).not.toBeInTheDocument();
  });
});
