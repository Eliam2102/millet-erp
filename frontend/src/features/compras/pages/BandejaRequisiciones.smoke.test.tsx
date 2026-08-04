import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaRequisiciones } from '@/features/compras/pages/BandejaRequisiciones';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  Clasificacion,
  EstadoRequisicion,
  OrigenRequisicion,
  Prioridad,
} from '@/features/compras/api/types';

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
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
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

describe('<BandejaRequisiciones> — smoke', () => {
  it('estado loading: muestra el header', () => {
    mswServer.use(
      http.get(
        '*/api/v1/compras/requisiciones',
        () =>
          new Promise(() => {
            /* never resolves */
          }),
      ),
    );
    render(<BandejaRequisiciones />, { wrapper: createQueryWrapper() });
    // Existen al menos 2 ocurrencias de "Requisiciones" (breadcrumb +
    // header) — getAllByText evita el ambiguity.
    expect(screen.getAllByText(/Requisiciones/i).length).toBeGreaterThan(0);
  });

  it('estado empty: muestra "Aún no tienes requisiciones"', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    render(<BandejaRequisiciones />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/aún no tienes requisiciones/i),
      ).toBeInTheDocument(),
    );
  });

  it('estado con datos: renderiza la tabla con folios', async () => {
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
    render(<BandejaRequisiciones />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('MID2026-000001')).toBeInTheDocument(),
    );
  });

  it('muestra el badge "Sistema" solo en las RQ de origen Sistema', async () => {
    const base = {
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
    };
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones', () =>
        HttpResponse.json({
          items: [
            {
              ...base,
              id: 'rq-manual',
              folio: 'MID2026-000010',
              origen: OrigenRequisicion.Manual,
            },
            {
              ...base,
              id: 'rq-sistema',
              folio: 'MID2026-000011',
              origen: OrigenRequisicion.Sistema,
            },
          ],
          offset: 0,
          limit: 50,
          total: 2,
        }),
      ),
    );
    render(<BandejaRequisiciones />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('MID2026-000011')).toBeInTheDocument(),
    );
    // Exactamente un badge "Sistema": el de la RQ de sistema; la manual no lo lleva.
    expect(screen.getAllByText('Sistema')).toHaveLength(1);
  });

  it('estado error: muestra ErrorState con problem detail', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Error de servidor',
            status: 500,
            detail: 'Algo falló en el backend',
          },
          {
            status: 500,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );
    render(<BandejaRequisiciones />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Error de servidor')).toBeInTheDocument(),
    );
    expect(screen.getByText(/Algo falló en el backend/i)).toBeInTheDocument();
  });

  it('botón "Nueva requisición" visible con permiso crear', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    render(<BandejaRequisiciones />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        // design/frontend-polish: el botón ahora abre un Sheet en lugar
        // de navegar a /nueva, así que es <button>, no <a>.
        screen.getByRole('button', { name: /nueva requisición/i }),
      ).toBeInTheDocument(),
    );
  });

  it('axe-core: cero violations WCAG 2.1 AA en estado vacío', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/requisiciones', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
      ),
    );
    const { container } = render(<BandejaRequisiciones />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() =>
      expect(
        screen.getByText(/aún no tienes requisiciones/i),
      ).toBeInTheDocument(),
    );
    const results: AxeResults = await axe.run(container, {
      runOnly: {
        type: 'tag',
        values: ['wcag2a', 'wcag2aa', 'wcag21aa'],
      },
    });
    if (results.violations.length > 0) {
      const detalle = results.violations
        .map((v) => `${v.id} (${v.impact}): ${v.description}`)
        .join('\n');
      throw new Error(`axe-core violations:\n${detalle}`);
    }
    expect(results.violations).toHaveLength(0);
  });
});
