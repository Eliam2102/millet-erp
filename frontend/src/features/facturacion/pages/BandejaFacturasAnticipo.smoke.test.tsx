import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaFacturasAnticipo } from '@/features/facturacion/pages/BandejaFacturasAnticipo';
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
    permisos: ['facturacion.anticipos.leer'],
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

describe('<BandejaFacturasAnticipo> — smoke (ANT-PR2)', () => {
  it('estado empty', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/anticipos/facturas', () =>
        HttpResponse.json({ items: [], sinAsignarCount: null }),
      ),
    );
    render(<BandejaFacturasAnticipo />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin facturas de anticipo/i)).toBeInTheDocument(),
    );
  });

  it('renderiza folio, saldo y estado del anticipo (13-H)', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/anticipos/facturas', () =>
        HttpResponse.json({
          items: [
            {
              id: 'fa-1',
              folio: 'FANT-2026-000003',
              estado: 'Timbrado',
              uuid: '11111111-2222-3333-4444-555555555555',
              receptorNombre: 'Cliente Maquila',
              receptorRfc: 'AAA010101AAA',
              tipoAnticipo: 'ClientesMxp',
              total: 1160,
              moneda: 'MXN',
              fechaTimbrado: '2026-07-12T10:00:00Z',
              estadoAnticipo: 'Abierto',
              saldo: 660,
            },
          ],
          sinAsignarCount: null,
        }),
      ),
    );
    render(<BandejaFacturasAnticipo />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('FANT-2026-000003')).toBeInTheDocument(),
    );
    expect(screen.getByText('Cliente Maquila')).toBeInTheDocument();
    expect(screen.getByText('660.00')).toBeInTheDocument();
    expect(screen.getByText('Abierto')).toBeInTheDocument();
    expect(screen.getByText('Timbrado')).toBeInTheDocument();
  });

  it('estado error', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/anticipos/facturas', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Boom', status: 500 },
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<BandejaFacturasAnticipo />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/No se pudieron cargar las facturas de anticipo/i),
      ).toBeInTheDocument(),
    );
  });
});
