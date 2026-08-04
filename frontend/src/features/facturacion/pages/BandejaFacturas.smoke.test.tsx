import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaFacturas } from '@/features/facturacion/pages/BandejaFacturas';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['facturacion.facturas.leer', 'facturacion.facturas.emitir'],
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

describe('<BandejaFacturas> — smoke', () => {
  it('estado empty + link Nueva factura visible con permiso', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas', () =>
        HttpResponse.json({ items: [], sinAsignarCount: null }),
      ),
    );
    render(<BandejaFacturas />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin facturas/i)).toBeInTheDocument(),
    );
    // FAC-UX-PR2: navega a /facturacion/facturas/nueva (ya no abre Sheet).
    const link = screen.getByRole('link', { name: /Nueva factura/i });
    expect(link).toHaveAttribute('href', '/facturacion/facturas/nueva');
  });

  it('renderiza una factura timbrada con folio y receptor', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas', () =>
        HttpResponse.json({
          items: [
            {
              id: 'f-1',
              folio: 'A-1',
              estado: 'Timbrado',
              uuid: '11111111-2222-3333-4444-555555555555',
              receptorNombre: 'Público en general',
              receptorRfc: 'XAXX010101000',
              total: 116,
              moneda: 'MXN',
              fechaTimbrado: '2026-05-30T10:00:00Z',
            },
          ],
          sinAsignarCount: null,
        }),
      ),
    );
    render(<BandejaFacturas />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Público en general')).toBeInTheDocument(),
    );
    expect(screen.getByText('A-1')).toBeInTheDocument();
    expect(screen.getByText('Timbrado')).toBeInTheDocument();
  });

  it('estado error', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Boom', status: 500 },
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<BandejaFacturas />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/No se pudieron cargar las facturas/i),
      ).toBeInTheDocument(),
    );
  });
});
