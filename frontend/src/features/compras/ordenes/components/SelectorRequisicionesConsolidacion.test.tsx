import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SelectorRequisicionesConsolidacion } from '@/features/compras/ordenes/components/SelectorRequisicionesConsolidacion';
import { useAuthStore } from '@/lib/auth/auth-store';
import type { RequisicionDisponible } from '@/features/compras/ordenes/api/useRequisicionesDisponibles';

function makeRq(
  overrides: Partial<RequisicionDisponible> = {},
): RequisicionDisponible {
  return {
    id: '00000099-0000-0000-0000-000000000001',
    folio: 'MID2026-000050',
    folioAnio: 2026,
    departamentoId: 'd-1',
    requisitanteId: 'u-1',
    fechaSolicitud: '2026-05-09T10:00:00Z',
    fechaEntregaDeseada: null,
    totalLineas: 3,
    proveedorSugeridoId: null,
    ...overrides,
  };
}

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
    permisos: ['compras.ordenes.crear'],
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

describe('<SelectorRequisicionesConsolidacion> (UF2-PR2)', () => {
  it('open=false: no renderiza nada', () => {
    render(
      <SelectorRequisicionesConsolidacion
        open={false}
        onOpenChange={() => {}}
        sucursalId="s-1"
        rqsPreviamenteSeleccionadas={[]}
        onConfirm={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(
      screen.queryByText(/Seleccionar RQs para consolidar/i),
    ).not.toBeInTheDocument();
  });

  it('sucursalId null: muestra empty state pidiendo seleccionar sucursal', () => {
    render(
      <SelectorRequisicionesConsolidacion
        open={true}
        onOpenChange={() => {}}
        sucursalId={null}
        rqsPreviamenteSeleccionadas={[]}
        onConfirm={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(
      screen.getByText(/Selecciona la sucursal de destino/i),
    ).toBeInTheDocument();
  });

  it('200 OK con RQs: renderiza folios + permite seleccionar y confirmar', async () => {
    mswServer.use(
      http.get(
        '*/api/v1/compras/ordenes/requisiciones-disponibles',
        ({ request }) => {
          const url = new URL(request.url);
          expect(url.searchParams.get('sucursalId')).toBe('s-1');
          return HttpResponse.json([
            makeRq({ id: 'rq-1', folio: 'MID2026-000001' }),
            makeRq({ id: 'rq-2', folio: 'MID2026-000002', totalLineas: 5 }),
          ]);
        },
      ),
    );

    let confirmadas: readonly RequisicionDisponible[] | null = null;
    render(
      <SelectorRequisicionesConsolidacion
        open={true}
        onOpenChange={() => {}}
        sucursalId="s-1"
        rqsPreviamenteSeleccionadas={[]}
        onConfirm={(rqs) => {
          confirmadas = rqs;
        }}
      />,
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() =>
      expect(screen.getByText('MID2026-000001')).toBeInTheDocument(),
    );
    expect(screen.getByText('MID2026-000002')).toBeInTheDocument();

    // Marca la primera RQ.
    const checkbox1 = screen.getByLabelText(/Seleccionar MID2026-000001/i);
    fireEvent.click(checkbox1);

    // El botón "Agregar 1 RQ" debe estar habilitado.
    const submitBtn = screen.getByRole('button', { name: /Agregar 1 RQ/i });
    expect(submitBtn).not.toBeDisabled();
    fireEvent.click(submitBtn);

    expect(confirmadas).not.toBeNull();
    const list = confirmadas as unknown as RequisicionDisponible[];
    expect(list).toHaveLength(1);
    expect(list[0].id).toBe('rq-1');
  });

  it('toggle-all: marca todas las visibles; segunda vez las desmarca', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/requisiciones-disponibles', () =>
        HttpResponse.json([
          makeRq({ id: 'rq-1', folio: 'MID2026-000001' }),
          makeRq({ id: 'rq-2', folio: 'MID2026-000002' }),
        ]),
      ),
    );

    render(
      <SelectorRequisicionesConsolidacion
        open={true}
        onOpenChange={() => {}}
        sucursalId="s-1"
        rqsPreviamenteSeleccionadas={[]}
        onConfirm={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() =>
      expect(screen.getByText('MID2026-000001')).toBeInTheDocument(),
    );

    // Radix Dialog usa portal: el contenido vive en document.body, NO
    // en el container retornado por render. Buscamos por aria-label
    // que es estable.
    const toggleAllBtn = screen.getByLabelText(
      /Seleccionar todos los RQs visibles/i,
    );
    fireEvent.click(toggleAllBtn);

    expect(screen.getByText(/2 de 2 seleccionadas/i)).toBeInTheDocument();

    fireEvent.click(toggleAllBtn);
    expect(screen.getByText(/0 de 2 seleccionadas/i)).toBeInTheDocument();
  });

  it('búsqueda por folio: filtra client-side sobre los items', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/requisiciones-disponibles', () =>
        HttpResponse.json([
          makeRq({ id: 'rq-1', folio: 'MID2026-000001' }),
          makeRq({ id: 'rq-2', folio: 'CUN2026-000099' }),
        ]),
      ),
    );

    render(
      <SelectorRequisicionesConsolidacion
        open={true}
        onOpenChange={() => {}}
        sucursalId="s-1"
        rqsPreviamenteSeleccionadas={[]}
        onConfirm={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() =>
      expect(screen.getByText('MID2026-000001')).toBeInTheDocument(),
    );

    const search = screen.getByLabelText(/Buscar RQs por folio/i);
    fireEvent.change(search, { target: { value: 'CUN' } });

    expect(screen.queryByText('MID2026-000001')).not.toBeInTheDocument();
    expect(screen.getByText('CUN2026-000099')).toBeInTheDocument();
  });

  it('preselección: arranca con los IDs marcados', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/requisiciones-disponibles', () =>
        HttpResponse.json([
          makeRq({ id: 'rq-1', folio: 'MID2026-000001' }),
          makeRq({ id: 'rq-2', folio: 'MID2026-000002' }),
        ]),
      ),
    );

    render(
      <SelectorRequisicionesConsolidacion
        open={true}
        onOpenChange={() => {}}
        sucursalId="s-1"
        rqsPreviamenteSeleccionadas={[
          makeRq({ id: 'rq-2', folio: 'MID2026-000002' }),
        ]}
        onConfirm={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() =>
      expect(screen.getByText(/1 de 2 seleccionadas/i)).toBeInTheDocument(),
    );
  });

  it('lista vacía del backend: muestra empty state OC-específico', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/requisiciones-disponibles', () =>
        HttpResponse.json([]),
      ),
    );

    render(
      <SelectorRequisicionesConsolidacion
        open={true}
        onOpenChange={() => {}}
        sucursalId="s-1"
        rqsPreviamenteSeleccionadas={[]}
        onConfirm={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() =>
      expect(
        screen.getByText(/No hay RQs autorizadas disponibles/i),
      ).toBeInTheDocument(),
    );
  });
});
