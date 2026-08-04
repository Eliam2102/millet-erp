import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ProveedorDetalle } from '@/modules/datos-maestros/components/ProveedorDetalle';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke del detalle de proveedor (P3 del patrón cross-módulo). Mockea
 * <c>@tanstack/react-router</c> con <c>useParams</c> que devuelve un
 * id estable y <c>Link</c> reemplazado por anchor.
 *
 * <para>Verifica que tras llegar la respuesta, el header muestra la
 * clave + el badge "Activo" + razón social, y el form aparece con el
 * RFC del proveedor mockeado.</para>
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
  useParams: () => ({ id: 'p-1' }),
}));

const PROVEEDOR_DETALLE = {
  id: 'p-1',
  clave: 'P-001',
  claveLegacy: null,
  razonSocial: 'Acme S.A. de C.V.',
  nombreComercial: 'Acme',
  rfc: 'ACM010101ABC',
  tipoPersona: 0,
  condicionesPagoDias: 30,
  monedaPreferidaId: null,
  email: 'contacto@acme.mx',
  telefono: '+52 55 0000 0000',
  estatus: 0,
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
      PermisosCanonicos.DatosMaestrosProveedoresGestionar,
      PermisosCanonicos.CompartidoCatalogosAdministrar,
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

describe('<ProveedorDetalle> — smoke', () => {
  it('renderiza header con clave + badge Activo y form con RFC', async () => {
    mswServer.use(
      http.get('*/api/v1/datos-maestros/proveedores/p-1', () =>
        HttpResponse.json(PROVEEDOR_DETALLE),
      ),
    );

    render(<ProveedorDetalle />, { wrapper: createQueryWrapper() });

    // Header: la clave aparece cuando data llegó.
    await waitFor(() =>
      expect(screen.getByText('P-001')).toBeInTheDocument(),
    );

    // Badge "Activo" presente.
    expect(screen.getByText('Activo')).toBeInTheDocument();

    // Form: input con el RFC default.
    const rfcInput = screen.getByDisplayValue('ACM010101ABC');
    expect(rfcInput).toBeInTheDocument();

    // Razón social también visible.
    const razonInput = screen.getByDisplayValue('Acme S.A. de C.V.');
    expect(razonInput).toBeInTheDocument();

    // Botón Desactivar visible (proveedor activo + permiso).
    expect(
      screen.getByRole('button', { name: /^desactivar$/i }),
    ).toBeInTheDocument();
  });

  it('oculta el botón Desactivar cuando el proveedor está inactivo', async () => {
    mswServer.use(
      http.get('*/api/v1/datos-maestros/proveedores/p-1', () =>
        HttpResponse.json({ ...PROVEEDOR_DETALLE, estatus: 1 }),
      ),
    );

    render(<ProveedorDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText('P-001')).toBeInTheDocument());

    expect(
      screen.queryByRole('button', { name: /^desactivar$/i }),
    ).not.toBeInTheDocument();
    expect(screen.getByText('Inactivo')).toBeInTheDocument();
  });
});
