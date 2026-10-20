import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { PendientesLayout } from '@/features/compras/pages/PendientesLayout';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  Clasificacion,
  DisponibilidadPendiente,
  EstadoRequisicion,
  NivelAutorizacion,
  OrigenRequisicion,
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
    'aria-current'?: React.AriaAttributes['aria-current'];
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

describe('<PendientesLayout> — smoke', () => {
  it('sin idActivo + sin items: placeholder en el panel detalle', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    render(<PendientesLayout idActivo={null} />, {
      wrapper: createQueryWrapper(),
    });
    expect(await screen.findByText(/selecciona una requisición pendiente/i)).toBeInTheDocument();
  });

  it('sin items: muestra EmptyState "no hay requisiciones pendientes"', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    render(<PendientesLayout idActivo={null} />, {
      wrapper: createQueryWrapper(),
    });
    expect(await screen.findByText(/no hay requisiciones pendientes/i)).toBeInTheDocument();
  });

  it('con datos: renderiza la lista compacta con folio + EstadoBadge', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json({
          items: [
            {
              id: 'rq-p1',
              folio: 'MID2026-000050',
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
            },
          ],
          offset: 0,
          limit: 50,
          total: 1,
        }),
      ),
    );
    render(<PendientesLayout idActivo={null} />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(screen.getByText('MID2026-000050')).toBeInTheDocument());
  });

  it('con idActivo + detalle: monta el detalle en el panel derecho', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    render(<PendientesLayout idActivo="rq-1" detalle={<div>DETALLE-PENDIENTE</div>} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByText('DETALLE-PENDIENTE')).toBeInTheDocument();
  });

  it('error del backend: muestra ErrorState con retry', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Error de servidor',
            status: 500,
            detail: 'Algo falló',
          },
          {
            status: 500,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );
    render(<PendientesLayout idActivo={null} />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(screen.getByText('Error de servidor')).toBeInTheDocument());
  });
  it('con indicadores: renderiza badges de disponibilidad, recurrente y nivel del item', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/pendientes-autorizacion', () =>
        HttpResponse.json({
          items: [
            {
              id: 'rq-ind',
              folio: 'MID2026-000077',
              folioAnio: 2026,
              estado: EstadoRequisicion.EnAutorizacion,
              clasificacion: Clasificacion.Servicio,
              prioridad: Prioridad.Normal,
              sucursalId: 's-1',
              departamentoId: 'd-1',
              requisitanteId: 'u-99',
              requisitanteNombre: 'Pepe Pérez',
              departamentoNombre: 'Compras',
              departamentoClave: 'COMPRAS',
              descripcion: null,
              fechaSolicitud: '2026-05-09T10:00:00Z',
              fechaEntregaDeseada: null,
              situacionSurtido: null,
              nivelPendiente: NivelAutorizacion.Nivel2,
              origen: OrigenRequisicion.Manual,
              disponibilidad: DisponibilidadPendiente.Parcial,
              esRecurrente: true,
            },
          ],
          offset: 0,
          limit: 50,
          total: 1,
        }),
      ),
    );
    render(<PendientesLayout idActivo={null} />, {
      wrapper: createQueryWrapper(),
    });
    expect(await screen.findByText('MID2026-000077')).toBeInTheDocument();
    expect(screen.queryByText('Stock parcial')).not.toBeInTheDocument();
    expect(screen.getByText('Recurrente')).toBeInTheDocument();
    expect(screen.getByText('Recurrente').closest('a')?.querySelectorAll('[data-slot=badge]')).toHaveLength(2);
    expect(screen.getByText('Falta N2')).toBeInTheDocument();
  });
});
