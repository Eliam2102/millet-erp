import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SaldosJerarquiaPage } from '@/features/almacen/pages/SaldosJerarquiaPage';
import { useAuthStore } from '@/lib/auth/auth-store';

const { mockNavigate, searchState } = vi.hoisted(() => ({
  mockNavigate: vi.fn(),
  searchState: { current: {} as Record<string, unknown> },
}));

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => mockNavigate,
  useSearch: () => searchState.current,
}));

const SUCURSALES = [
  {
    tipo: 'sucursal',
    id: 's-1',
    clave: 'CONKAL',
    nombre: 'Planta Conkal',
    cantidad: 35,
    valorInventarioMxn: 6300,
    esDefault: false,
    esHoja: false,
  },
  {
    tipo: 'sucursal',
    id: 's-2',
    clave: 'MERIDA',
    nombre: 'Planta Mérida',
    cantidad: 5,
    valorInventarioMxn: 300,
    esDefault: false,
    esHoja: false,
  },
];

beforeEach(() => {
  searchState.current = {};
  mockNavigate.mockClear();
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['almacen.almacenes.leer'],
    errorMessage: null,
  });

  mswServer.use(
    http.get('*/api/v1/almacen/almacenes', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 }),
    ),
    http.get('*/api/v1/almacen/sub-almacenes', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 }),
    ),
    http.get('*/api/v1/almacen/saldos/jerarquia', () =>
      HttpResponse.json(SUCURSALES),
    ),
  );
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

describe('<SaldosJerarquiaPage> — consulta jerárquica (PR6)', () => {
  it('default modo ubicación: browse desde la raíz sin exigir selección', async () => {
    render(<SaldosJerarquiaPage />, { wrapper: createQueryWrapper() });

    expect(screen.getByText('Consulta jerárquica')).toBeInTheDocument();
    // El árbol raíz carga solo (2 sucursales; no auto-expande al ramificar).
    await waitFor(() => expect(screen.getByText('CONKAL')).toBeInTheDocument());
    expect(screen.getByText('MERIDA')).toBeInTheDocument();
  });

  it('modo artículo sin selección: empty-state que pide artículo', () => {
    searchState.current = { modo: 'articulo' };
    render(<SaldosJerarquiaPage />, { wrapper: createQueryWrapper() });

    expect(screen.getByText('Elige un artículo')).toBeInTheDocument();
    expect(screen.queryByText('CONKAL')).not.toBeInTheDocument();
  });

  it('el toggle "Incluir vacíos" viaja por la URL (navigate con search)', async () => {
    render(<SaldosJerarquiaPage />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('CONKAL')).toBeInTheDocument());

    fireEvent.click(screen.getByLabelText('Incluir vacíos'));

    expect(mockNavigate).toHaveBeenCalledWith(
      expect.objectContaining({
        to: '/almacen/saldos-jerarquia',
        search: expect.objectContaining({ incluirVacios: true }),
      }),
    );
  });
});
