import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaRepp } from '@/features/facturacion/pages/BandejaRepp';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

const abrirSpy = vi.fn();
vi.mock('@/features/facturacion/components/nuevo-repp-context', () => ({
  useNuevoRepp: () => ({ abrir: abrirSpy, cerrar: () => {}, setDirty: () => {} }),
}));

beforeEach(() => {
  abrirSpy.mockClear();
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['facturacion.facturas.leer', 'facturacion.repp.emitir'],
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

describe('<BandejaRepp> — smoke', () => {
  it('lista un REPP y muestra Nuevo REPP', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/repp', () =>
        HttpResponse.json({
          items: [
            {
              id: 'r-1',
              folio: 'REPP-1',
              estado: 'Timbrado',
              uuid: 'U-1',
              receptorNombre: 'Cliente Demo SA',
              importeTotalPago: 1000,
              fechaPago: '2026-05-30T12:00:00Z',
            },
          ],
          sinAsignarCount: null,
        }),
      ),
    );
    render(<BandejaRepp />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Cliente Demo SA')).toBeInTheDocument(),
    );
    expect(screen.getByText('REPP-1')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Nuevo REPP/i }),
    ).toBeInTheDocument();
  });

  it('empty', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/repp', () =>
        HttpResponse.json({ items: [], sinAsignarCount: null }),
      ),
    );
    render(<BandejaRepp />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin complementos de pago/i)).toBeInTheDocument(),
    );
  });
});
