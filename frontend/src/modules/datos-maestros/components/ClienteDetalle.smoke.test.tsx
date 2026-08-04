import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ClienteDetalle } from '@/modules/datos-maestros/components/ClienteDetalle';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke del detalle de cliente (P3 del patrón cross-módulo, ADR-0048).
 * Mockea <c>@tanstack/react-router</c> con <c>useParams</c> que
 * devuelve un id estable y <c>Link</c> reemplazado por anchor.
 *
 * <para>Verifica que tras llegar la respuesta, el header muestra la
 * clave + badges (Activo, A+W, Fiscales incompletos) y el form aparece
 * con la razón social del cliente mockeado.</para>
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
  useParams: () => ({ id: 'c-1' }),
}));

const CLIENTE_DETALLE = {
  id: 'c-1',
  clave: 'CLI-001',
  referenciaExterna: 'AW-777',
  razonSocial: 'Vidrios del Centro S.A. de C.V.',
  rfc: null,
  regimenFiscal: null,
  codigoPostalFiscal: null,
  usoCfdiDefault: null,
  formaPagoDefault: null,
  metodoPagoDefault: null,
  monedaDefault: 'MXN',
  esGenerico: false,
  origen: 1,
  email: null,
  telefono: null,
  datosFiscalesCompletos: false,
  estatus: 0,
};

beforeEach(() => {
  // Los selectores de catálogo del form (moneda, régimen, uso CFDI,
  // forma de pago) disparan su query al montar; lista vacía basta.
  mswServer.use(
    http.get('*/api/v1/catalogos/monedas', () => HttpResponse.json([])),
    http.get('*/api/v1/catalogos/regimenes-fiscales', () =>
      HttpResponse.json([]),
    ),
    http.get('*/api/v1/catalogos/usos-cfdi', () => HttpResponse.json([])),
    http.get('*/api/v1/catalogos/formas-pago', () => HttpResponse.json([])),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.DatosMaestrosClientesGestionar],
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

describe('<ClienteDetalle> — smoke', () => {
  it('renderiza header con clave + badges A+W/fiscales y form con razón social', async () => {
    mswServer.use(
      http.get('*/api/v1/datos-maestros/clientes/c-1', () =>
        HttpResponse.json(CLIENTE_DETALLE),
      ),
    );

    render(<ClienteDetalle />, { wrapper: createQueryWrapper() });

    // Header: la clave aparece cuando data llegó.
    await waitFor(() =>
      expect(screen.getByText('CLI-001')).toBeInTheDocument(),
    );

    // Badges: Activo + origen A+W + chip ámbar de fiscales incompletos.
    expect(screen.getByText('Activo')).toBeInTheDocument();
    expect(screen.getByText('A+W')).toBeInTheDocument();
    expect(screen.getByText('Fiscales incompletos')).toBeInTheDocument();

    // Form: input con la razón social.
    expect(
      screen.getByDisplayValue('Vidrios del Centro S.A. de C.V.'),
    ).toBeInTheDocument();

    // Referencia externa read-only visible.
    expect(screen.getByDisplayValue('AW-777')).toBeInTheDocument();

    // Botón Desactivar visible (cliente activo + permiso).
    expect(
      screen.getByRole('button', { name: /^desactivar$/i }),
    ).toBeInTheDocument();
  });

  it('oculta el chip fiscal y el botón Desactivar cuando corresponde', async () => {
    mswServer.use(
      http.get('*/api/v1/datos-maestros/clientes/c-1', () =>
        HttpResponse.json({
          ...CLIENTE_DETALLE,
          rfc: 'VCE010101AAA',
          regimenFiscal: '601',
          codigoPostalFiscal: '76100',
          datosFiscalesCompletos: true,
          estatus: 1,
        }),
      ),
    );

    render(<ClienteDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('CLI-001')).toBeInTheDocument(),
    );

    expect(
      screen.queryByText('Fiscales incompletos'),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /^desactivar$/i }),
    ).not.toBeInTheDocument();
    expect(screen.getByText('Inactivo')).toBeInTheDocument();
  });
});
