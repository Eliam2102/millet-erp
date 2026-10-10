import { afterEach, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { SucursalesLayout } from './SucursalesLayout';

vi.mock('@/lib/auth/useAuth', () => ({ useAuth: () => useAuthStore.getState() }));
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children }: { children: React.ReactNode }) => <a>{children}</a>,
}));
afterEach(() => useAuthStore.getState().clearSession());

it('usa la razón social de la sesión y no ofrece crear empresa (CA1.1)', async () => {
  const empresaId = '00000003-0000-0000-0000-000000000001';
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'sesion-prueba',
    currentEmpresaId: empresaId,
    empresas: [
      { id: empresaId, rfc: 'MIL010101ABC', razonSocial: 'Empresa de prueba P6', esLaActual: true },
    ],
    permisos: [P.AdminEmpresasLeer, P.AdminEmpresasSucursalesGestionar],
  });
  mswServer.use(
    http.get(`*/api/v1/admin/empresas/${empresaId}`, () =>
      HttpResponse.json({
        empresa: { id: empresaId, razonSocial: 'Razón social anterior' },
        sucursales: [],
        departamentos: [],
      }),
    ),
  );
  render(<SucursalesLayout idActivo={null} />, { wrapper: createQueryWrapper() });
  expect(await screen.findByText('Empresa de prueba P6')).toBeVisible();
  expect(screen.queryByText('Vidrios Millet')).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Agregar sucursal' })).toBeVisible();
  expect(
    screen.queryByRole('button', { name: /crear empresa|nueva empresa/i }),
  ).not.toBeInTheDocument();
});
