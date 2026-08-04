import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ProductoAwDetalle } from '@/modules/datos-maestros/components/ProductoAwDetalle';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke del detalle de producto A+W (P3 del patrón cross-módulo,
 * ADR-0048). Mockea <c>@tanstack/react-router</c> con <c>useParams</c>
 * que devuelve un id estable y <c>Link</c> reemplazado por anchor.
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
  useParams: () => ({ id: 'pa-1' }),
}));

const PRODUCTO_DETALLE = {
  id: 'pa-1',
  referenciaExterna: 'VID-TEMP-6MM',
  descripcion: 'Vidrio templado 6 mm',
  unidadMedida: 'M2',
  unidadMedidaId: null,
  categoriaId: null,
  claveProdServSat: null,
  claveUnidadSat: null,
  objetoImp: null,
  tasaIvaTraslado: null,
  tasaRetencionIva: null,
  tasaRetencionIsr: null,
  origen: 1,
  datosFiscalesCompletos: false,
  estatus: 0,
};

beforeEach(() => {
  // El form monta <UnidadMedidaSelect> y <CategoriaSelector>, que
  // disparan sus queries al montar; listas vacías bastan.
  mswServer.use(
    http.get('*/api/v1/catalogos/unidades-medida', () =>
      HttpResponse.json([]),
    ),
    http.get('*/api/v1/catalogos/categorias-articulo', () =>
      HttpResponse.json([]),
    ),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.DatosMaestrosProductosAwGestionar],
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

describe('<ProductoAwDetalle> — smoke', () => {
  it('renderiza header con referencia + badges y form con descripción', async () => {
    mswServer.use(
      http.get('*/api/v1/datos-maestros/productos-aw/pa-1', () =>
        HttpResponse.json(PRODUCTO_DETALLE),
      ),
    );

    render(<ProductoAwDetalle />, { wrapper: createQueryWrapper() });

    // Header: la referencia aparece cuando data llegó (header + form
    // read-only comparten el valor).
    await waitFor(() =>
      expect(screen.getAllByText('VID-TEMP-6MM').length).toBeGreaterThan(0),
    );

    // Badges: Activo + origen A+W + chip ámbar (sin claves SAT).
    expect(screen.getByText('Activo')).toBeInTheDocument();
    expect(screen.getByText('A+W')).toBeInTheDocument();
    expect(screen.getByText('Fiscales incompletos')).toBeInTheDocument();

    // Form: input con la descripción.
    expect(
      screen.getByDisplayValue('Vidrio templado 6 mm'),
    ).toBeInTheDocument();

    // Botón Desactivar visible (producto activo + permiso).
    expect(
      screen.getByRole('button', { name: /^desactivar$/i }),
    ).toBeInTheDocument();
  });

  it('oculta el chip fiscal cuando las claves SAT están completas', async () => {
    mswServer.use(
      http.get('*/api/v1/datos-maestros/productos-aw/pa-1', () =>
        HttpResponse.json({
          ...PRODUCTO_DETALLE,
          claveProdServSat: '43211701',
          claveUnidadSat: 'M2',
          datosFiscalesCompletos: true,
        }),
      ),
    );

    render(<ProductoAwDetalle />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getAllByText('VID-TEMP-6MM').length).toBeGreaterThan(0),
    );

    expect(
      screen.queryByText('Fiscales incompletos'),
    ).not.toBeInTheDocument();
  });
});
