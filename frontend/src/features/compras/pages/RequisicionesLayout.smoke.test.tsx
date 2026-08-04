import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { RequisicionesLayout } from '@/features/compras/pages/RequisicionesLayout';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  Clasificacion,
  EstadoRequisicion,
  Prioridad,
} from '@/features/compras/api/types';

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    params,
    className,
    'aria-current': ariaCurrent,
  }: {
    children: React.ReactNode;
    to: string;
    params?: Record<string, string>;
    className?: string;
    'aria-current'?: string;
  }) => (
    <a
      href={params != null ? `${to.replace('$id', params.id ?? '')}` : to}
      className={className}
      aria-current={ariaCurrent}
    >
      {children}
    </a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({ offset: 0, limit: 50 }),
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
  setupCatalogos();
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['compras.requisiciones.leer', 'compras.requisiciones.crear'],
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

describe('<RequisicionesLayout> — smoke', () => {
  it('sin idActivo: muestra placeholder "Selecciona una requisición"', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    render(<RequisicionesLayout idActivo={null} />, {
      wrapper: createQueryWrapper(),
    });
    expect(
      await screen.findByText(/selecciona una requisición/i),
    ).toBeInTheDocument();
  });

  it('con idActivo + detalle: renderiza el detalle en el panel', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    render(
      <RequisicionesLayout idActivo="rq-1" detalle={<div>DETALLE-X</div>} />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByText('DETALLE-X')).toBeInTheDocument();
  });

  it('lista con items: cada item es un Link con folio y EstadoBadge', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones', () =>
        HttpResponse.json({
          items: [
            {
              id: 'rq-1',
              folio: 'MID2026-000001',
              folioAnio: 2026,
              estado: EstadoRequisicion.Borrador,
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
    render(<RequisicionesLayout idActivo={null} />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() =>
      expect(screen.getByText('MID2026-000001')).toBeInTheDocument(),
    );
  });

  it('idActivo presente: el item activo lleva aria-current="page"', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones', () =>
        HttpResponse.json({
          items: [
            {
              id: 'rq-activo',
              folio: 'MID2026-000099',
              folioAnio: 2026,
              estado: EstadoRequisicion.Borrador,
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
    render(
      <RequisicionesLayout idActivo="rq-activo" detalle={<div>D</div>} />,
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() =>
      expect(screen.getByText('MID2026-000099')).toBeInTheDocument(),
    );
    // El Link mock devuelve <a aria-current="page">; encontramos el
    // que tiene ese atributo.
    const activo = screen
      .getByText('MID2026-000099')
      .closest('a');
    expect(activo).toHaveAttribute('aria-current', 'page');
  });
});
