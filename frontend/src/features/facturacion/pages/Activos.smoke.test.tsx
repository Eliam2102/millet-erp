import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { Activos } from '@/features/facturacion/pages/Activos';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 't',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u', email: 't@e.com', nombre: 'T' },
    empresas: [],
    currentEmpresaId: 'e',
    permisos: ['facturacion.activos.autorizar'],
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

describe('<Activos> — smoke', () => {
  it('lista autorizaciones y muestra "Autorizar venta" con permiso', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/activos/autorizaciones', () =>
        HttpResponse.json([
          {
            id: 'au-1',
            activoRef: 'AF-1',
            descripcion: 'Montacargas',
            precioVenta: 50000,
            valorNetoEnLibros: 30000,
            utilidadOPerdida: 20000,
            autorizadoPor: 'u-1',
            fechaAutorizacion: '2026-05-30T10:00:00Z',
            estado: 'Autorizada',
          },
        ]),
      ),
    );
    render(<Activos />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Montacargas')).toBeInTheDocument(),
    );
    expect(
      screen.getByRole('button', { name: /Autorizar venta/i }),
    ).toBeInTheDocument();
  });

  it('empty', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/activos/autorizaciones', () =>
        HttpResponse.json([]),
      ),
    );
    render(<Activos />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin autorizaciones/i)).toBeInTheDocument(),
    );
  });
});
