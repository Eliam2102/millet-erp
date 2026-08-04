import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { CapturaConteoPage } from '@/features/almacen/pages/CapturaConteoPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EstadoConteo } from '@/features/almacen/api/types';

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    className,
  }: {
    children: React.ReactNode;
    to: string;
    className?: string;
  }) => (
    <a href={to} className={className}>
      {children}
    </a>
  ),
  useParams: () => ({ id: 'c-1' }),
}));

const CONTEO_BASE = {
  id: 'c-1',
  tipo: 0,
  estado: EstadoConteo.EnCurso,
  fechaPlanificada: '2026-05-24',
  subAlmacenId: null,
  filtroFamilia: null,
  responsableId: 'u-1',
  fechaInicio: '2026-05-24T08:00:00Z',
  snapshotCapturadoAt: '2026-05-24T08:00:00Z',
  aprobadorId: null,
  fechaAprobacion: null,
  version: 1,
  cantidadLineas: 2,
  cantidadCapturadas: 0,
};

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['almacen.inventarios.capturar', 'almacen.inventarios.leer'],
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

describe('<CapturaConteoPage> — captura sin sesgo (A6)', () => {
  it('estado loading muestra el banner sin-sesgo desde el inicio', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/conteos/c-1', () =>
        HttpResponse.json(CONTEO_BASE),
      ),
      http.get(
        '*/api/v1/almacen/conteos/c-1/lineas-para-capturar',
        () => new Promise(() => {}), // never resolves
      ),
    );
    render(<CapturaConteoPage />, { wrapper: createQueryWrapper() });
    expect(
      screen.getByText(/Captura sin sesgo \(política A6\)/i),
    ).toBeInTheDocument();
  });

  it('lista líneas SIN cantidad teórica (verificable por payload)', async () => {
    // Mock devuelve solo los campos del DTO de captura — NO incluye
    // cantidadTeorica. La prueba verifica que la pantalla renderiza
    // las filas y NO contiene texto "teórica" en la tabla (solo en
    // el banner A6 explicativo).
    mswServer.use(
      http.get('*/api/v1/almacen/conteos/c-1', () =>
        HttpResponse.json({ ...CONTEO_BASE, cantidadLineas: 2 }),
      ),
      http.get(
        '*/api/v1/almacen/conteos/c-1/lineas-para-capturar',
        () =>
          HttpResponse.json([
            {
              id: 'l-1',
              articuloId: 'a-1',
              ubicacionId: 'u-1',
              ubicacionClave: 'HG1-05',
              subAlmacenId: 's-1',
              cantidadRealCapturada: null,
              requiereRecuento: false,
              articuloClave: 'IPP60001',
              articuloDescripcion: 'Aceite de corte',
              subAlmacenClave: 'HG1',
            },
            {
              id: 'l-2',
              articuloId: 'a-2',
              ubicacionId: 'u-1',
              ubicacionClave: 'HG1-05',
              subAlmacenId: 's-1',
              cantidadRealCapturada: null,
              requiereRecuento: true,
              articuloClave: 'ACC86024',
              articuloDescripcion: 'PVB acústico',
              subAlmacenClave: 'HG1',
            },
          ]),
      ),
    );
    render(<CapturaConteoPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('IPP60001')).toBeInTheDocument(),
    );
    expect(screen.getByText('ACC86024')).toBeInTheDocument();
    // Nunca GUID: con la clave resuelta, el id crudo no se pinta.
    expect(screen.queryByText('a-1')).not.toBeInTheDocument();
    expect(screen.queryByText('s-1')).not.toBeInTheDocument();
    // El header "Cantidad real" existe, pero NO debe existir un
    // header "Cantidad teórica" — ese vive en la pantalla de
    // aprobación separada.
    expect(
      screen.getByRole('columnheader', { name: /Cantidad real/i }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('columnheader', { name: /Teórica/i }),
    ).toBeNull();
  });

  it('estado no-capturable (Aprobado) muestra mensaje informativo', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/conteos/c-1', () =>
        HttpResponse.json({ ...CONTEO_BASE, estado: EstadoConteo.Aprobado }),
      ),
      http.get('*/api/v1/almacen/conteos/c-1/lineas-para-capturar', () =>
        HttpResponse.json([]),
      ),
    );
    render(<CapturaConteoPage />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/la captura solo se permite mientras esté/i),
      ).toBeInTheDocument(),
    );
  });

  it('axe-core: cero violations en estado con datos', async () => {
    mswServer.use(
      http.get('*/api/v1/almacen/conteos/c-1', () =>
        HttpResponse.json(CONTEO_BASE),
      ),
      http.get('*/api/v1/almacen/conteos/c-1/lineas-para-capturar', () =>
        HttpResponse.json([
          {
            id: 'l-1',
            articuloId: 'a-1',
            subAlmacenId: 's-1',
            ubicacionId: 'u-1',
            ubicacionClave: 'HG1-05',
            cantidadRealCapturada: null,
            requiereRecuento: false,
            articuloClave: 'IPP60001',
            articuloDescripcion: 'Aceite de corte',
            subAlmacenClave: 'HG1',
          },
        ]),
      ),
    );
    const { container } = render(<CapturaConteoPage />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() =>
      expect(screen.getByText('IPP60001')).toBeInTheDocument(),
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
