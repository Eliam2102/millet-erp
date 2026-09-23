import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DepartamentosTopLevelPage } from '@/modules/administracion/components/DepartamentosTopLevelPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke de <c>/admin/departamentos</c> (ADR-0051) — la home top-level
 * que reemplaza al tab "Departamentos" de <c>EmpresaDetalle</c>. Lee
 * contra la empresa activa de la sesión, sin selector.
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
}));

const EMPRESA_DETALLE = {
  empresa: {
    id: 'e-1',
    rfc: 'MIL010101ABC',
    razonSocial: 'Millet S.A. de C.V.',
    nombreComercial: null,
    regimenFiscal: '601',
    activa: true,
    version: 1,
  },
  sucursales: [],
  departamentos: [
    { id: 'd-1', clave: 'D01', nombre: 'Ventas', estatus: 0, version: 1 },
    { id: 'd-2', clave: 'D02', nombre: 'Producción', estatus: 0, version: 1 },
  ],
};

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [
      { id: 'e-1', rfc: 'MIL010101ABC', razonSocial: 'Millet S.A. de C.V.', esLaActual: true },
    ],
    currentEmpresaId: 'e-1',
    permisos: [
      PermisosCanonicos.AdminEmpresasLeer,
      PermisosCanonicos.AdminDepartamentosGestionar,
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

describe('<DepartamentosTopLevelPage> — smoke', () => {
  it('renderiza el catálogo de departamentos y el link avanzado a datos de la empresa', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/empresas/e-1', () =>
        HttpResponse.json(EMPRESA_DETALLE),
      ),
    );

    render(<DepartamentosTopLevelPage />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('Ventas')).toBeInTheDocument(),
    );
    expect(screen.getByText('Producción')).toBeInTheDocument();

    const linkAvanzado = screen.getByRole('link', {
      name: /datos de la empresa/i,
    });
    expect(linkAvanzado).toHaveAttribute('href', '/admin/empresas/$id');
  });

  it('permite agregar departamento cuando el usuario tiene el permiso', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/empresas/e-1', () =>
        HttpResponse.json(EMPRESA_DETALLE),
      ),
    );

    render(<DepartamentosTopLevelPage />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: /agregar departamento/i }),
      ).toBeInTheDocument(),
    );
  });
});
