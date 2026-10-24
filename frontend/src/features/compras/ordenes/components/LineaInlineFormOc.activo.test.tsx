import { beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { LineaInlineFormOc } from '@/features/compras/ordenes/components/LineaInlineFormOc';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  EstadoOrdenCompra,
  type LineaOrdenCompraResponse,
  type OrdenCompraDetalleResponse,
} from '@/features/compras/ordenes/api/types';

const ART = '22222222-2222-2222-2222-222222222222';
const DEP = '44444444-4444-4444-4444-444444444444';
const CLASE = '33333333-3333-3333-3333-333333333333';

const linea = (extra: Partial<LineaOrdenCompraResponse> = {}) =>
  ({
    id: 'l-1',
    posicion: 1,
    requisicionId: null,
    articuloId: ART,
    cantidad: 5,
    unidadMedida: 'LT',
    precioUnitario: 100,
    departamentoSolicitanteId: DEP,
    descripcionExtendida: null,
    fechaEntregaLinea: null,
    textoAdicional: null,
    centroCostoId: '0c000000-0000-0000-0000-000000000001',
    fraccionArancelaria: null,
    cantidadRecibida: 0,
    esActivoFijo: true,
    claseActivoId: CLASE,
    ...extra,
  }) as unknown as LineaOrdenCompraResponse;

const OC = {
  id: 'oc-1',
  esImportacion: false,
  estado: EstadoOrdenCompra.Borrador,
} as unknown as OrdenCompraDetalleResponse;

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/catalogos/articulos', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
    ),
    http.get('*/api/v1/activos-fijos/clases', () =>
      HttpResponse.json([{ id: CLASE, nombre: 'Maquinaria', activa: true }]),
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

describe('<LineaInlineFormOc> — activo fijo en edición', () => {
  it('prellena y envía esActivoFijo/claseActivoId en el PATCH', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.patch('*/api/v1/compras/ordenes/oc-1/lineas/l-1', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    render(<LineaInlineFormOc oc={OC} linea={linea()} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    const chk = screen.getByRole('checkbox', { name: /activo fijo/i });
    expect(chk).toBeChecked();
    expect(chk).toBeEnabled();
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));
    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ esActivoFijo: true, claseActivoId: CLASE });
  });

  it('deshabilita los campos si la línea ya tiene recepciones', () => {
    render(
      <LineaInlineFormOc oc={OC} linea={linea({ cantidadRecibida: 1 })} onCancel={() => {}} />,
      {
        wrapper: createQueryWrapper(),
      },
    );
    expect(screen.getByRole('checkbox', { name: /activo fijo/i })).toBeDisabled();
  });
});
