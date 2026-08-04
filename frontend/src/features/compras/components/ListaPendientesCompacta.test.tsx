import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ListaPendientesCompacta } from '@/features/compras/components/ListaPendientesCompacta';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  Clasificacion,
  EstadoRequisicion,
  Prioridad,
  type RequisicionListItemResponse,
} from '@/features/compras/api/types';
import type { PendientesSearch } from '@/features/compras/lib/pendientes-search-schema';

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
}));

function makeItem(
  overrides: Partial<RequisicionListItemResponse> = {},
): RequisicionListItemResponse {
  return {
    id: 'rq-x',
    folio: 'MID2026-000010',
    folioAnio: 2026,
    estado: EstadoRequisicion.EnAutorizacion,
    clasificacion: Clasificacion.Servicio,
    prioridad: Prioridad.Normal,
    sucursalId: 's-1',
    departamentoId: 'd-1',
    requisitanteId: 'u-99',
    descripcion: null,
    fechaSolicitud: '2026-05-09T10:00:00Z',
    fechaEntregaDeseada: null,
    requisitanteNombre: 'Pepe Pérez',
    departamentoNombre: 'Compras y Adquisiciones',
    departamentoClave: 'COMPRAS',
    ...overrides,
  };
}

const SEARCH_VACIA: PendientesSearch = { offset: 0, limit: 50 };

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/identidad/usuarios', () =>
      HttpResponse.json({
        items: [{ id: 'u-99', nombre: 'Pepe Pérez', email: 'p@m.com' }],
        offset: 0,
        limit: 200,
        total: 1,
      }),
    ),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'u@m.com', nombre: 'Test' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [],
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

describe('<ListaPendientesCompacta>', () => {
  it('renderiza folio y muestra el requisitanteNombre del backend', () => {
    render(
      <ListaPendientesCompacta
        items={[makeItem()]}
        idActivo={null}
        search={SEARCH_VACIA}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByText('MID2026-000010')).toBeInTheDocument();
    // El nombre del requisitante viene resuelto del backend (ADR-0042),
    // sin leer el padrón de usuarios en el cliente.
    expect(screen.getByText(/Pepe Pérez/)).toBeInTheDocument();
  });

  it('cae al requisitanteId si el backend no resolvió el nombre', () => {
    render(
      <ListaPendientesCompacta
        items={[makeItem({ requisitanteId: 'u-sin-nombre', requisitanteNombre: null })]}
        idActivo={null}
        search={SEARCH_VACIA}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByText('u-sin-nombre')).toBeInTheDocument();
  });

  it('item activo lleva aria-current="page"', () => {
    render(
      <ListaPendientesCompacta
        items={[makeItem({ id: 'rq-activo' })]}
        idActivo="rq-activo"
        search={SEARCH_VACIA}
      />,
      { wrapper: createQueryWrapper() },
    );
    const link = screen.getByText('MID2026-000010').closest('a');
    expect(link).toHaveAttribute('aria-current', 'page');
  });

  it('items no-activos NO llevan aria-current', () => {
    render(
      <ListaPendientesCompacta
        items={[makeItem({ id: 'rq-x' })]}
        idActivo="otro-id"
        search={SEARCH_VACIA}
      />,
      { wrapper: createQueryWrapper() },
    );
    const link = screen.getByText('MID2026-000010').closest('a');
    expect(link).not.toHaveAttribute('aria-current', 'page');
  });
});
