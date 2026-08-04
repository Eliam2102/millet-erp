import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SaldosPage } from '@/features/almacen/pages/SaldosPage';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/almacen/sub-almacenes', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 }),
    ),
    // El <ArticuloSelector> del filtro consulta este catálogo al montar.
    http.get('*/api/v1/catalogos/articulos', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
    ),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['almacen.almacenes.leer'],
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

describe('<SaldosPage> — smoke', () => {
  it('estado empty muestra "Sin saldos"', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/saldos', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 }),
      ),
    );
    render(<SaldosPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin saldos/i)).toBeInTheDocument(),
    );
  });

  it('columna muestra clave·nombre cuando vienen, y cae al id cuando son null', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/saldos', () =>
        HttpResponse.json({
          items: [
            {
              subAlmacenId: 'sa-1',
              articuloId: 'a-1',
              cantidad: 100,
              cantidadDisponible: 90,
              costoPromedioMxn: 50,
              valorInventarioMxn: 5000,
              // (a) enriquecido → la columna muestra clave·nombre, NO el id.
              articuloClave: 'ACC-001',
              articuloDescripcion: 'Tornillo M6',
            },
            {
              subAlmacenId: 'sa-1',
              articuloId: 'a-2',
              cantidad: 50,
              cantidadDisponible: 50,
              costoPromedioMxn: 200,
              valorInventarioMxn: 10000,
              // (b) sin resolver → la columna cae al id.
              articuloClave: null,
              articuloDescripcion: null,
            },
          ],
          offset: 0,
          limit: 500,
          total: 2,
        }),
      ),
    );
    render(<SaldosPage />, { wrapper: createQueryWrapper() });
    // (a) clave + nombre presentes; el id 'a-1' ya NO se muestra.
    await waitFor(() =>
      expect(screen.getByText('ACC-001')).toBeInTheDocument(),
    );
    expect(screen.getByText(/Tornillo M6/)).toBeInTheDocument();
    expect(screen.queryByText('a-1')).not.toBeInTheDocument();
    // (b) fallback al id en la fila sin clave/nombre.
    expect(screen.getByText('a-2')).toBeInTheDocument();
    // Total valor 5000 + 10000 = 15000.
    expect(screen.getByText(/\$15,000\.00/)).toBeInTheDocument();
  });

  it('(c) el filtro de artículo es el ArticuloSelector, no el input de GUID', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/saldos', () =>
        HttpResponse.json({ items: [], offset: 0, limit: 500, total: 0 }),
      ),
    );
    render(<SaldosPage />, { wrapper: createQueryWrapper() });
    // El selector está presente (combobox con su aria-label)…
    await waitFor(() =>
      expect(
        screen.getByRole('combobox', { name: /Seleccionar artículo/i }),
      ).toBeInTheDocument(),
    );
    // …y el viejo input de GUID ya no existe.
    expect(
      screen.queryByPlaceholderText('GUID del artículo'),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByLabelText('Filtrar por artículo'),
    ).not.toBeInTheDocument();
  });

  it('axe-core: cero violations en estado con datos', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/saldos', () =>
        HttpResponse.json({
          items: [
            {
              subAlmacenId: 'sa-1',
              articuloId: 'a-1',
              cantidad: 100,
              cantidadDisponible: 90,
              costoPromedioMxn: 50,
              valorInventarioMxn: 5000,
            },
          ],
          offset: 0,
          limit: 500,
          total: 1,
        }),
      ),
    );
    const { container } = render(<SaldosPage />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(screen.getByText('a-1')).toBeInTheDocument());
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
