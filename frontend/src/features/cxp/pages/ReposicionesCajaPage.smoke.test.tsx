import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ReposicionesCajaPage } from '@/features/cxp/pages/ReposicionesCajaPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { DestinoReposicionCaja } from '@/features/cxp/api/types';

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['cuentas_por_pagar.reposiciones.leer', 'cuentas_por_pagar.reposiciones.administrar'],
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

describe('<ReposicionesCajaPage> — sin mínimo', () => {
  it('muestra saldos heredados y no ofrece configurar mínimo', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-pagar/reposiciones-caja/saldos', () =>
        HttpResponse.json([
          {
            sucursalId: 's-1',
            destino: DestinoReposicionCaja.CuentaSucursal,
            moneda: 'MXN',
            saldo: 350,
            numeroComprobaciones: 2,
          },
        ]),
      ),
      http.get('*/api/v1/cuentas-por-pagar/reposiciones-caja', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    render(<ReposicionesCajaPage />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByRole('button', { name: /Emitir ahora/ })).toBeInTheDocument());
    expect(screen.getByText('2')).toBeInTheDocument();
    expect(screen.queryByText(/mínimo/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Guardar mínimo/i })).not.toBeInTheDocument();
  });
});
