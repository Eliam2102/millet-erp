import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { NotasCargoPage } from '@/features/cxp/pages/NotasCargoPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EstadoNotaCargo } from '@/features/cxp/api/types';

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
    permisos: [
      'cuentas_por_pagar.notas-cargo.leer',
      'cuentas_por_pagar.notas-cargo.crear',
      'cuentas_por_pagar.notas-cargo.autorizar',
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

describe('<NotasCargoPage> — smoke', () => {
  it('Borrador no ofrece Autorizar en la fila: la regla del creador vive en el detalle', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/notas-cargo', () =>
        HttpResponse.json({
          items: [
            {
              id: 'nc-1',
              folio: 'NCG-2026-000001',
              folioAnio: 2026,
              proveedorId: 'p-1',
              sucursalId: null,
              concepto: 'Reembolso por flete duplicado en OC 12345',
              monto: 1500,
              moneda: 'MXN',
              facturaOrigenId: null,
              devolucionAProveedorId: null,
              estado: EstadoNotaCargo.Borrador,
              fechaCreacion: '2026-05-20T00:00:00Z',
              version: 1,
              proveedorNombre: 'Fletes del Bajío SA',
              proveedorRfc: 'FBA010101AB1',
            },
          ],
          offset: 0,
          limit: 200,
          total: 1,
        }),
      ),
    );
    render(<NotasCargoPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/NCG-2026-000001/i),
      ).toBeInTheDocument(),
    );
    expect(screen.queryByRole('button', { name: /Autorizar/i })).toBeNull();
    // El folio es el enlace al detalle y la fila muestra proveedor con su RFC.
    expect(screen.getByRole('link', { name: /NCG-2026-000001/i })).toBeInTheDocument();
    expect(screen.getByText('FBA010101AB1')).toBeInTheDocument();
  });
});
