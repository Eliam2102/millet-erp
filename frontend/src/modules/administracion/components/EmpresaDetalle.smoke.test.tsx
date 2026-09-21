import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { EmpresaDetalle } from '@/modules/administracion/components/EmpresaDetalle';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke del detalle de empresa (P3 del patrón cross-módulo).
 *
 * <para>Mockea <c>@tanstack/react-router</c> con <c>useParams</c> que
 * devuelve un id estable y <c>Link</c> reemplazado por un anchor. El
 * test apunta a reproducir el bug reportado por Eduardo: en
 * <c>/admin/empresas/&lt;id&gt;</c> no aparecen las tabs Datos |
 * Sucursales | Departamentos. Si la respuesta del API es correcta y
 * el componente renderiza los tres botones de tab + el contenido del
 * tab activo, el test verde valida que el componente NO tiene un bug
 * estructural en su propio render.</para>
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
  sucursales: Array.from({ length: 23 }, (_, i) => ({
    id: `s-${i + 1}`,
    clave: `S${String(i + 1).padStart(2, '0')}`,
    nombre: `Sucursal ${i + 1}`,
    estatus: 0,
    version: 1,
  })),
  departamentos: Array.from({ length: 25 }, (_, i) => ({
    id: `d-${i + 1}`,
    clave: `D${String(i + 1).padStart(2, '0')}`,
    nombre: `Departamento ${i + 1}`,
    estatus: 0,
    version: 1,
  })),
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
  it('renderiza las tabs Datos | Sucursales (23) | Departamentos (25)', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/empresas/e-1', () =>
        HttpResponse.json(EMPRESA_DETALLE),
      ),
    );

    render(<EmpresaDetalle />, { wrapper: createQueryWrapper() });

    // Cabecera: el RFC aparece cuando data llegó.
    await waitFor(() =>
      expect(screen.getByText('BLO902640MHI')).toBeInTheDocument(),
    );

    // Las tres tabs deben estar en el DOM como <button role="tab">.
    const tabs = screen.getAllByRole('tab');
    expect(tabs).toHaveLength(3);

    // El texto de cada tab incluye el nombre y, en su caso, el contador.
    expect(
      screen.getByRole('tab', { name: /^datos$/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('tab', { name: /sucursales\s*\(23\)/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('tab', { name: /departamentos\s*\(25\)/i }),
    ).toBeInTheDocument();
  });
});

/**
 * Regresión F1-ADM-01: bug de corrupción silenciosa entre tenants.
 *
 * <para>Cuando la empresa activa de la sesión (<c>currentEmpresaId</c>)
 * no coincide con la empresa que se está viendo (<c>empresa.id</c> del
 * path), las altas de Sucursales/Departamentos deben quedar
 * bloqueadas — el backend resuelve la escritura contra la empresa
 * activa, no contra la que se ve — y debe aparecer un aviso
 * explicativo.</para>
 */
describe('<EmpresaDetalle> — bloqueo por empresa activa distinta', () => {
  beforeEach(() => {
    useAuthStore.setState({
      status: 'authenticated',
      accessToken: 'test-token',
      expiresAt: new Date(Date.now() + 3600_000),
      user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
      empresas: [
        { id: 'e-1', rfc: 'BLO902640MHI', razonSocial: 'Bloques de Occidente S.A. de C.V.', esLaActual: false },
        { id: 'e-OTRA', rfc: 'OTR010101AAA', razonSocial: 'Otra Empresa S.A. de C.V.', esLaActual: true },
      ],
      currentEmpresaId: 'e-OTRA',
      permisos: [
        PermisosCanonicos.AdminEmpresasLeer,
        PermisosCanonicos.AdminEmpresasEditar,
        PermisosCanonicos.AdminEmpresasSucursalesGestionar,
        PermisosCanonicos.AdminDepartamentosGestionar,
      ],
      errorMessage: null,
    });
  });

  it('bloquea "Agregar sucursal" y muestra el aviso cuando la empresa vista no es la activa', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/empresas/e-1', () =>
        HttpResponse.json(EMPRESA_DETALLE),
      ),
    );

    render(<EmpresaDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('BLO902640MHI')).toBeInTheDocument(),
    );

    fireEvent.click(screen.getByRole('tab', { name: /sucursales\s*\(23\)/i }));

    expect(
      screen.queryByRole('button', { name: /agregar sucursal/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByText(/tu empresa activa es/i),
    ).toBeInTheDocument();
  });

  it('bloquea "Agregar departamento" y muestra el aviso cuando la empresa vista no es la activa', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/empresas/e-1', () =>
        HttpResponse.json(EMPRESA_DETALLE),
      ),
    );

    render(<EmpresaDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('BLO902640MHI')).toBeInTheDocument(),
    );

    fireEvent.click(
      screen.getByRole('tab', { name: /departamentos\s*\(25\)/i }),
    );

    expect(
      screen.queryByRole('button', { name: /agregar departamento/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByText(/tu empresa activa es/i),
    ).toBeInTheDocument();
  });
});

describe('<EmpresaDetalle> — sin bloqueo cuando la empresa activa coincide', () => {
  beforeEach(() => {
    useAuthStore.setState({
      status: 'authenticated',
      accessToken: 'test-token',
      expiresAt: new Date(Date.now() + 3600_000),
      user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
      empresas: [
        { id: 'e-1', rfc: 'BLO902640MHI', razonSocial: 'Bloques de Occidente S.A. de C.V.', esLaActual: true },
      ],
      currentEmpresaId: 'e-1',
      permisos: [
        PermisosCanonicos.AdminEmpresasLeer,
        PermisosCanonicos.AdminEmpresasEditar,
        PermisosCanonicos.AdminEmpresasSucursalesGestionar,
        PermisosCanonicos.AdminDepartamentosGestionar,
      ],
      errorMessage: null,
    });
  });

  it('muestra "Agregar sucursal" cuando el usuario tiene permiso y la empresa vista es la activa', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/empresas/e-1', () =>
        HttpResponse.json(EMPRESA_DETALLE),
      ),
    );

    render(<EmpresaDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('BLO902640MHI')).toBeInTheDocument(),
    );

    fireEvent.click(screen.getByRole('tab', { name: /sucursales\s*\(23\)/i }));

    expect(
      screen.getByRole('button', { name: /agregar sucursal/i }),
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/tu empresa activa es/i),
    ).not.toBeInTheDocument();
  });
});
