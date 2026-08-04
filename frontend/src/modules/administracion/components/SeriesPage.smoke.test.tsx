import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SeriesPage } from '@/modules/administracion/components/SeriesPage';
import { NuevaSerieProvider } from '@/modules/administracion/components/SheetNuevaSerie';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke de la bandeja P1 de Series y folios (UF-Admin-PR6 §5.1).
 * Mockea el router porque el componente usa solo presentación + hooks
 * de TanStack Query.
 */
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    ...rest
  }: {
    children: React.ReactNode;
    to: string;
    [key: string]: unknown;
  }) => (
    <a href={to} {...rest}>
      {children}
    </a>
  ),
  useNavigate: () => () => undefined,
}));

const SERIE_OC = {
  id: 'srv-1',
  empresaId: 'e-1',
  sucursalId: null,
  tipoDocumento: 1, // OrdenCompra
  prefijo: 'OC',
  sufijo: null,
  reinicioPeriodo: 1, // Anual
  activa: true,
  version: 1,
};

const EMPRESA_E1 = {
  id: 'e-1',
  rfc: 'MIL010101ABC',
  razonSocial: 'Millet S.A. de C.V.',
  nombreComercial: 'Millet',
  regimenFiscal: '601',
  activa: true,
  version: 1,
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
      PermisosCanonicos.AdminSeriesGestionar,
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

describe('<SeriesPage> — smoke', () => {
  it('renderiza la tabla con una fila + botón "Nueva serie" cuando hay permiso', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/series', () =>
        HttpResponse.json({ items: [SERIE_OC], total: 1 }),
      ),
      http.get('*/api/v1/admin/empresas', () =>
        HttpResponse.json({ items: [EMPRESA_E1], total: 1 }),
      ),
    );

    render(
      <NuevaSerieProvider>
        <SeriesPage />
      </NuevaSerieProvider>,
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() =>
      expect(screen.getByText('OC')).toBeInTheDocument(),
    );
    // Tipo Documento label.
    expect(screen.getByText('Orden de compra')).toBeInTheDocument();
    // Reinicio "Anual".
    expect(screen.getByText('Anual')).toBeInTheDocument();
    // Estatus activa.
    expect(screen.getByText('Activa')).toBeInTheDocument();
    // Botón "Nueva serie" (puede aparecer en header y empty state — basta con uno).
    expect(
      screen.getAllByRole('button', { name: /nueva serie/i })[0],
    ).toBeInTheDocument();
  });

  it('al hacer click en Editar despliega el inline form (border amber)', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/series', () =>
        HttpResponse.json({ items: [SERIE_OC], total: 1 }),
      ),
      http.get('*/api/v1/admin/empresas', () =>
        HttpResponse.json({ items: [EMPRESA_E1], total: 1 }),
      ),
      // El inline form también lee detalle para el preview.
      http.get('*/api/v1/admin/series/srv-1', () =>
        HttpResponse.json({
          serie: SERIE_OC,
          proximoFolioPreview: 'OC-2026-000001',
        }),
      ),
    );

    const { container } = render(
      <NuevaSerieProvider>
        <SeriesPage />
      </NuevaSerieProvider>,
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() =>
      expect(screen.getByText('OC')).toBeInTheDocument(),
    );

    const editarBtn = screen.getByRole('button', { name: /^editar$/i });
    fireEvent.click(editarBtn);

    // El form inline aparece con aria-label "Editar serie OC".
    await waitFor(() =>
      expect(
        screen.getByRole('form', { name: /editar serie oc/i }),
      ).toBeInTheDocument(),
    );

    // Border amber (clase del form).
    const inlineForm = container.querySelector('form[aria-label*="Editar serie"]');
    expect(inlineForm?.className).toMatch(/border-amber/);
  });

  it('EmptyState aparece cuando no hay series', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/series', () =>
        HttpResponse.json({ items: [], total: 0 }),
      ),
      http.get('*/api/v1/admin/empresas', () =>
        HttpResponse.json({ items: [EMPRESA_E1], total: 1 }),
      ),
    );

    render(
      <NuevaSerieProvider>
        <SeriesPage />
      </NuevaSerieProvider>,
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() =>
      expect(screen.getByText(/aún no hay series/i)).toBeInTheDocument(),
    );
  });
});
