import '@testing-library/jest-dom/vitest';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { UsuarioDetalle } from '@/modules/identidad/components/UsuarioDetalle';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke del detalle de usuario (P3 del patrón cross-módulo). Mockea
 * <c>@tanstack/react-router</c> con <c>useParams</c> y reemplaza
 * <c>Link</c> por un anchor para no depender del Router en el test.
 *
 * <para>Apunta a verificar que las 3 tabs (Datos | Roles por empresa
 * | Preferencias) se renderizan y que la acción Desactivar aparece
 * cuando el usuario está activo y se tiene el permiso.</para>
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
  useParams: () => ({ id: 'u-1' }),
  useNavigate: () => vi.fn(),
}));

const USUARIO_DETALLE = {
  usuario: {
    id: 'u-1',
    email: 'admin@millet.com.mx',
    entraOid: 'dev-admin@millet.com.mx',
    nombre: 'Eduardo Paredes',
    departamentoId: null,
    activo: true,
    version: 1,
  },
  asignaciones: [
    {
      id: 'uer-1',
      empresaId: 'e-1',
      empresaRfc: 'BLO902640MHI',
      rolId: 'r-1',
      rolCodigo: 'admin',
      fechaAsignacion: '2026-05-01T12:00:00Z',
    },
    {
      id: 'uer-2',
      empresaId: 'e-2',
      empresaRfc: 'TIG010101AAA',
      rolId: 'r-1',
      rolCodigo: 'admin',
      fechaAsignacion: '2026-05-01T12:00:00Z',
    },
  ],
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
      PermisosCanonicos.IdentidadUsuariosLeer,
      PermisosCanonicos.IdentidadUsuariosEditar,
      PermisosCanonicos.IdentidadUsuariosDesactivar,
      PermisosCanonicos.IdentidadAsignacionesAdministrar,
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

describe('<UsuarioDetalle> — smoke', () => {
  it('renderiza las 3 tabs y el botón Desactivar para un usuario activo', async () => {
    mswServer.use(
      http.get('*/api/v1/identidad/usuarios/u-1', () =>
        HttpResponse.json(USUARIO_DETALLE),
      ),
    );

    render(<UsuarioDetalle />, { wrapper: createQueryWrapper() });

    // Cabecera: el email aparece cuando data llegó.
    await waitFor(() =>
      expect(screen.getByText('admin@millet.com.mx')).toBeInTheDocument(),
    );

    // Las tres tabs deben estar en el DOM como <button role="tab">.
    const tabs = screen.getAllByRole('tab');
    expect(tabs).toHaveLength(3);

    expect(
      screen.getByRole('tab', { name: /^datos$/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('tab', { name: /roles por empresa\s*\(2\)/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('tab', { name: /^preferencias$/i }),
    ).toBeInTheDocument();

    // Botón Desactivar visible (usuario activo + permiso).
    expect(
      screen.getByRole('button', { name: /^desactivar$/i }),
    ).toBeInTheDocument();
  });

  it('deshabilita el botón Desactivar cuando es la propia cuenta activa en sesión', async () => {
    useAuthStore.setState({
      user: {
        id: 'u-1',
        email: 'admin@millet.com.mx',
        nombre: 'Admin Millet',
      },
    });

    mswServer.use(
      http.get('*/api/v1/identidad/usuarios/u-1', () =>
        HttpResponse.json(USUARIO_DETALLE),
      ),
    );

    render(<UsuarioDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('admin@millet.com.mx')).toBeInTheDocument(),
    );

    const btnDesactivar = screen.getByRole('button', { name: /^desactivar$/i });
    expect(btnDesactivar).toBeDisabled();
    expect(btnDesactivar).toHaveAttribute(
      'title',
      'No puedes desactivar tu propia cuenta activa',
    );
  });

  it('deshabilita la asignación y revocación de roles cuando es la propia cuenta activa en sesión (SoD)', async () => {
    useAuthStore.setState({
      user: {
        id: 'u-1',
        email: 'admin@millet.com.mx',
        nombre: 'Admin Millet',
      },
    });

    mswServer.use(
      http.get('*/api/v1/identidad/usuarios/u-1', () =>
        HttpResponse.json(USUARIO_DETALLE),
      ),
    );

    render(<UsuarioDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('admin@millet.com.mx')).toBeInTheDocument(),
    );

    fireEvent.click(
      screen.getByRole('tab', { name: /roles por empresa\s*\(2\)/i }),
    );

    expect(
      screen.getByText(/segregación de funciones \(SoD\)/i),
    ).toBeInTheDocument();

    const btnAsignar = screen.getByRole('button', { name: /asignar rol/i });
    expect(btnAsignar).toBeDisabled();

    const btnsRevocar = screen.getAllByRole('button', { name: /^revocar/i });
    expect(btnsRevocar.length).toBeGreaterThan(0);
    btnsRevocar.forEach((btn) => {
      expect(btn).toBeDisabled();
    });
  });

  it('muestra Reactivar (no Desactivar) cuando el usuario está inactivo', async () => {
    mswServer.use(
      http.get('*/api/v1/identidad/usuarios/u-1', () =>
        HttpResponse.json({
          ...USUARIO_DETALLE,
          usuario: { ...USUARIO_DETALLE.usuario, activo: false },
        }),
      ),
    );

    render(<UsuarioDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('admin@millet.com.mx')).toBeInTheDocument(),
    );

    expect(
      screen.queryByRole('button', { name: /^desactivar$/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /reactivar/i }),
    ).toBeInTheDocument();
    expect(screen.getByText('Inactivo')).toBeInTheDocument();
  });

  it('sin permiso de Centros de Costo: el tab NO aparece (3 tabs)', async () => {
    // La beforeEach NO incluye centros_costo.asignaciones.administrar → el
    // gate del tab lo oculta. Las 3 tabs originales quedan intactas.
    mswServer.use(
      http.get('*/api/v1/identidad/usuarios/u-1', () =>
        HttpResponse.json(USUARIO_DETALLE),
      ),
    );

    render(<UsuarioDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('admin@millet.com.mx')).toBeInTheDocument(),
    );

    expect(screen.getAllByRole('tab')).toHaveLength(3);
    expect(
      screen.queryByRole('tab', { name: /centros de costo/i }),
    ).not.toBeInTheDocument();
  });
});

describe('<UsuarioDetalle> — tab Centros de Costo (con permiso)', () => {
  beforeEach(() => {
    useAuthStore.setState({
      status: 'authenticated',
      accessToken: 'test-token',
      expiresAt: new Date(Date.now() + 3600_000),
      user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
      empresas: [],
      currentEmpresaId: 'e-1',
      permisos: [
        PermisosCanonicos.IdentidadUsuariosLeer,
        PermisosCanonicos.CentrosCostoAsignacionesAdministrar,
      ],
      errorMessage: null,
    });
    mswServer.use(
      http.get('*/api/v1/identidad/usuarios/u-1', () =>
        HttpResponse.json(USUARIO_DETALLE),
      ),
    );
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

  it('renderiza 4 tabs con "Centros de Costo" entre Roles y Preferencias', async () => {
    render(<UsuarioDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('admin@millet.com.mx')).toBeInTheDocument(),
    );

    const tabs = screen.getAllByRole('tab');
    expect(tabs).toHaveLength(4);
    // Orden: Datos | Roles por empresa (n) | Centros de Costo | Preferencias.
    expect(tabs[2]).toHaveTextContent(/centros de costo/i);
    expect(tabs[3]).toHaveTextContent(/preferencias/i);
    // Sin contador (patrón "Preferencias"): solo el título.
    expect(tabs[2]).toHaveTextContent(/^centros de costo$/i);
  });

  it('click en "Centros de Costo" monta el panel con el usuarioId del detalle', async () => {
    let arbolPedido: string | null = null;
    mswServer.use(
      http.get(
        '*/api/v1/centros-costo/asignaciones/:usuarioId/arbol',
        ({ params }) => {
          arbolPedido = params.usuarioId as string;
          return HttpResponse.json({
            usuarioId: 'u-1',
            esAlcanceTotal: false,
            resumen: {
              dim1Vivas: 1, dim1Completas: 0, dim2Vivas: 1, dim2Completas: 0,
              dim3Vivas: 3, dim3Asignadas: 0,
            },
            dim1s: [
              {
                id: 'd1', clave: '101', nombre: 'CONKAL', estado: 0,
                dim3Vivas: 3, dim3Asignadas: 0, grupos: [],
              },
            ],
          });
        },
      ),
    );

    render(<UsuarioDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('admin@millet.com.mx')).toBeInTheDocument(),
    );

    fireEvent.click(screen.getByRole('tab', { name: /centros de costo/i }));

    // El panel carga el árbol del usuario del detalle (u-1 del useParams mock).
    await waitFor(() =>
      expect(screen.getByText('101 · CONKAL')).toBeInTheDocument(),
    );
    expect(arbolPedido).toBe('u-1');
  });
});
