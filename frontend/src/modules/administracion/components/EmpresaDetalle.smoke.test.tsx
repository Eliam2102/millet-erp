import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { EmpresaDetalle } from '@/modules/administracion/components/EmpresaDetalle';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke del detalle de empresa — desde ADR-0051 (multisucursal, una
 * sola empresa) ya no es un master-detail con tabs Sucursales /
 * Departamentos; esos ejes viven en <c>/admin/sucursales</c> y
 * <c>/admin/departamentos</c> (ver sus propios smoke tests para los
 * escenarios de bloqueo por empresa activa distinta, migrados desde
 * aquí). Esta pantalla solo muestra/edita los datos generales de la
 * empresa (RFC, régimen fiscal, razón social).
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
  useParams: () => ({ id: 'e-1' }),
}));

const EMPRESA_DETALLE = {
  empresa: {
    id: 'e-1',
    rfc: 'BLO902640MHI',
    razonSocial: 'Bloques de Occidente S.A. de C.V.',
    nombreComercial: null,
    regimenFiscal: '601',
    activa: true,
    version: 2,
  },
  sucursales: [],
  departamentos: [],
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
      PermisosCanonicos.AdminEmpresasLeer,
      PermisosCanonicos.AdminEmpresasEditar,
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

describe('<EmpresaDetalle> — smoke', () => {
  it('renderiza el header con RFC y el form de datos generales, sin tabs', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/empresas/e-1', () =>
        HttpResponse.json(EMPRESA_DETALLE),
      ),
    );

    render(<EmpresaDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('BLO902640MHI')).toBeInTheDocument(),
    );

    // Ya no hay tabs — la pantalla es solo "Datos".
    expect(screen.queryAllByRole('tab')).toHaveLength(0);
    expect(
      screen.getByDisplayValue('Bloques de Occidente S.A. de C.V.'),
    ).toBeInTheDocument();
    expect(screen.getByDisplayValue('601')).toBeInTheDocument();
  });
});
