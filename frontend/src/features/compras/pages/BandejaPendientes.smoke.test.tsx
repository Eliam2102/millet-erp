import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaPendientes } from '@/features/compras/pages/BandejaPendientes';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  Clasificacion,
  EstadoRequisicion,
  NivelAutorizacion,
  Prioridad,
} from '@/features/compras/api/types';

// Holder mutable para variar los search params por test (TanStack useSearch).
const searchHolder = vi.hoisted(() => ({
  current: { offset: 0, limit: 50 } as Record<string, unknown>,
}));

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    className,
    state,
  }: {
    children: React.ReactNode;
    to: string;
    className?: string;
    state?: unknown;
  }) => (
    <a href={to} className={className} data-state={JSON.stringify(state)}>
      {children}
    </a>
  ),
  useNavigate: () => () => {},
  useSearch: () => searchHolder.current,
}));

function setupCatalogos() {
  mswServer.use(
    http.get('*/api/v1/catalogos/departamentos', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
    http.get('*/api/v1/identidad/usuarios', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
  );
}

beforeEach(() => {
  searchHolder.current = { offset: 0, limit: 50 };
  setupCatalogos();
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['compras.requisiciones.autorizar-nivel1'],
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

describe('<BandejaPendientes> — smoke', () => {
  it('estado empty: muestra texto específico de pendientes', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    render(<BandejaPendientes />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/no hay requisiciones pendientes/i),
      ).toBeInTheDocument(),
    );
  });

  it('estado con datos: renderiza la tabla con folios', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json({
          items: [
            {
              id: 'rq-1',
              folio: 'MID2026-000050',
              folioAnio: 2026,
              estado: EstadoRequisicion.EnAutorizacion,
              clasificacion: Clasificacion.Servicio,
              prioridad: Prioridad.Normal,
              sucursalId: 's-1',
              departamentoId: 'd-1',
              requisitanteId: 'u-1',
              descripcion: null,
              fechaSolicitud: '2026-05-09T10:00:00Z',
              fechaEntregaDeseada: null,
            },
          ],
          offset: 0,
          limit: 50,
          total: 1,
        }),
      ),
    );
    render(<BandejaPendientes />, { wrapper: createQueryWrapper() });
    // El folio aparece en la tabla (desktop) Y en la card (mobile);
    // ambas se renderizan en el DOM y CSS oculta una según breakpoint.
    await waitFor(() =>
      expect(screen.getAllByText('MID2026-000050').length).toBeGreaterThan(0),
    );
  });

  it('header "Pendientes de autorización" presente', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    render(<BandejaPendientes />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getAllByText(/Pendientes de autorización/i).length,
      ).toBeGreaterThan(0),
    );
  });

  it('muestra el badge de nivel pendiente por item (PR-A)', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json({
          items: [
            {
              id: 'rq-1',
              folio: 'MID2026-000051',
              folioAnio: 2026,
              estado: EstadoRequisicion.EnAutorizacion,
              clasificacion: Clasificacion.Servicio,
              prioridad: Prioridad.Normal,
              sucursalId: 's-1',
              departamentoId: 'd-1',
              requisitanteId: 'u-1',
              descripcion: null,
              fechaSolicitud: '2026-05-09T10:00:00Z',
              fechaEntregaDeseada: null,
              nivelPendiente: NivelAutorizacion.Nivel2,
            },
          ],
          offset: 0,
          limit: 50,
          total: 1,
        }),
      ),
    );
    render(<BandejaPendientes />, { wrapper: createQueryWrapper() });
    // Aparece en tabla (desktop) y card (mobile); ambas se renderizan.
    await waitFor(() =>
      expect(screen.getAllByText('Falta N2').length).toBeGreaterThan(0),
    );
  });

  it('el filtro por nivel pendiente está presente', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    render(<BandejaPendientes />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Todos los niveles')).toBeInTheDocument(),
    );
  });

  it('search.nivelPendiente se propaga al request (query param)', async () => {
    searchHolder.current = { nivelPendiente: 2, offset: 0, limit: 50 };
    let urlVista = '';
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', ({ request }) => {
        urlVista = request.url;
        return HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 });
      }),
    );
    render(<BandejaPendientes />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(urlVista).toContain('nivelPendiente=2'));
  });
});
