import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { MovimientosTcPage } from '@/features/cxp/pages/MovimientosTcPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  EstadoMovimientoTc,
  TipoMovimientoTc,
} from '@/features/cxp/api/types';

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
      'cuentas_por_pagar.tc.leer',
      'cuentas_por_pagar.tc.registrar-movimiento',
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

describe('<MovimientosTcPage> — smoke', () => {
  it('renderiza movimiento Flujo A con link a factura', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/movimientos-tc', () =>
        HttpResponse.json({
          items: [
            {
              id: 'm-1',
              tarjetaId: 't-1',
              usuarioQueUsoId: 'u-1',
              fechaMovimiento: '2026-05-22',
              tipo: TipoMovimientoTc.CompraConCfdi,
              estado: EstadoMovimientoTc.Registrado,
              montoOriginal: 1234,
              monedaOriginal: 'MXN',
              tipoCambioCaptura: null,
              montoMxn: 1234,
              merchantNormalizado: 'AMAZON MX',
              facturaProveedorId: 'f-abc',
              conceptoContable: 'Suministros oficina',
              version: 1,
            },
          ],
          offset: 0,
          limit: 200,
          total: 1,
        }),
      ),
    );
    render(<MovimientosTcPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/AMAZON MX/i)).toBeInTheDocument(),
    );
    expect(screen.getByText(/Compra con CFDI/i)).toBeInTheDocument();
  });
});
