import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaPendientesOc } from '@/features/compras/ordenes/pages/BandejaPendientesOc';
import { EstadoOrdenCompra } from '@/features/compras/ordenes/api/types';
import { useAuthStore } from '@/lib/auth/auth-store';

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
}));

function oc(id: string, folio: string, extra: Record<string, unknown>) {
  return {
    id,
    folio,
    folioAnio: 2026,
    estado: EstadoOrdenCompra.EnAutorizacionJefeCompras,
    proveedorId: 'prov-1',
    proveedorNombre: 'Vidrios del Norte',
    sucursalId: 's-1',
    compradorTitularId: 'u-1',
    moneda: 'MXN',
    fechaDocumento: '2026-10-01',
    referenciaProveedor: null,
    cantidadTotal: 12,
    numeroLineas: 2,
    totalAPagar: 15000,
    esMenorCuantia: false,
    esRecurrente: false,
    proveedorConocido: false,
    requiereNivel2: true,
    ...extra,
  };
}

const ocA = oc('oc-1', 'OC-2026-000001', { esMenorCuantia: true, proveedorConocido: true, requiereNivel2: false });
const ocB = oc('oc-2', 'OC-2026-000002', { totalAPagar: 80000 });

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/catalogos/proveedores', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 1000, total: 0 }),
    ),
    http.get('*/api/v1/identidad/usuarios', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['compras.ordenes.autorizar-nivel1'],
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

describe('<BandejaPendientesOc>', () => {
  it('seleccionar 2 OC + "Aprobar seleccionadas" envía un POST masivo N1 y muestra resultado por fila', async () => {
    const llamadas: { ids: string[]; nivel: number; key: string | null }[] = [];
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/pendientes-autorizacion', () =>
        HttpResponse.json({ items: [ocA, ocB], page: 1, pageSize: 50, totalCount: 2 }),
      ),
      http.post(
        '*/api/v1/compras/ordenes/autorizaciones-masivas',
        async ({ request }) => {
          const body = (await request.json()) as { ids: string[]; nivel: number };
          llamadas.push({
            ids: body.ids,
            nivel: body.nivel,
            key: request.headers.get('Idempotency-Key'),
          });
          return HttpResponse.json({
            resultados: body.ids.map((id) =>
              id === 'oc-1'
                ? { id, ok: true, errorCodigo: null, mensaje: null }
                : {
                    id,
                    ok: false,
                    errorCodigo: 'OC_NO_AUTORIZABLE',
                    mensaje: 'La OC ya no está en autorización',
                  },
            ),
          });
        },
      ),
    );

    render(<BandejaPendientesOc />, { wrapper: createQueryWrapper() });

    const checkA = await screen.findByRole('checkbox', { name: 'Seleccionar OC-2026-000001' });
    fireEvent.click(checkA);
    fireEvent.click(screen.getByRole('checkbox', { name: 'Seleccionar OC-2026-000002' }));
    fireEvent.click(screen.getByRole('button', { name: 'Aprobar seleccionadas (2)' }));

    await waitFor(() => expect(llamadas).toHaveLength(1));
    expect(llamadas[0]).toEqual({
      ids: ['oc-1', 'oc-2'],
      nivel: 1,
      key: expect.any(String),
    });

    const filaA = document.querySelector('tr[data-oc="oc-1"]') as HTMLElement;
    const filaB = document.querySelector('tr[data-oc="oc-2"]') as HTMLElement;
    expect(await within(filaA).findByRole('status')).toHaveTextContent('Aprobada');
    expect(await within(filaB).findByRole('status')).toHaveTextContent(
      'La OC ya no está en autorización',
    );
    expect(
      screen.getByRole('button', { name: 'Aprobar seleccionadas (1)' }),
    ).toBeInTheDocument();
  });

  it('botón "Aprobar" rápido solo en OC de menor cuantía o proveedor conocido; envía 1 id', async () => {
    const llamadas: string[][] = [];
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/pendientes-autorizacion', () =>
        HttpResponse.json({ items: [ocA, ocB], page: 1, pageSize: 50, totalCount: 2 }),
      ),
      http.post(
        '*/api/v1/compras/ordenes/autorizaciones-masivas',
        async ({ request }) => {
          const body = (await request.json()) as { ids: string[] };
          llamadas.push(body.ids);
          return HttpResponse.json({
            resultados: body.ids.map((id) => ({ id, ok: true, errorCodigo: null, mensaje: null })),
          });
        },
      ),
    );

    render(<BandejaPendientesOc />, { wrapper: createQueryWrapper() });

    expect(
      await screen.findByRole('button', { name: 'Aprobar OC-2026-000001' }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Aprobar OC-2026-000002' }),
    ).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Aprobar OC-2026-000001' }));
    await waitFor(() => expect(llamadas).toEqual([['oc-1']]));
  });

  it('sin permiso de autorización: casillas deshabilitadas y sin botón Aprobar', async () => {
    useAuthStore.setState({ permisos: ['compras.ordenes.leer'] });
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/pendientes-autorizacion', () =>
        HttpResponse.json({ items: [ocA], page: 1, pageSize: 50, totalCount: 1 }),
      ),
    );

    render(<BandejaPendientesOc />, { wrapper: createQueryWrapper() });

    const checkbox = await screen.findByRole('checkbox', { name: 'Seleccionar OC-2026-000001' });
    expect(checkbox).toBeDisabled();
    expect(
      screen.queryByRole('button', { name: 'Aprobar OC-2026-000001' }),
    ).not.toBeInTheDocument();
  });
});
