import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { EditorLineas } from '@/features/compras/ordenes/components/EditorLineas';
import { useAuthStore } from '@/lib/auth/auth-store';
import { EstadoOrdenCompra } from '@/features/compras/ordenes/api/types';
import type {
  LineaOrdenCompraResponse,
  OrdenCompraDetalleResponse,
} from '@/features/compras/ordenes/api/types';

/**
 * Fase E PR3 — columna CC-Máquina en la tabla de líneas de OC. El detalle
 * renderiza el CC ya resuelto por el read-port (ADR-0050): "clave — nombre"
 * si resuelve, "No catalogado" si el id no tiene fila (dato roto), "—" si la
 * línea no lleva CC.
 */

function makeLinea(
  overrides: Partial<LineaOrdenCompraResponse> = {},
): LineaOrdenCompraResponse {
  return {
    id: 'l-1',
    posicion: 1,
    articuloId: '22222222-2222-2222-2222-222222222222',
    articuloClave: null,
    articuloNombre: null,
    descripcionExtendida: null,
    cantidad: 5,
    unidadMedida: 'PZA',
    precioUnitario: 100,
    ivaImporte: 80,
    retencionIsr: null,
    subtotalLinea: 500,
    departamentoSolicitanteId: 'dep-1',
    centroCostoId: null,
    centroCostoClave: null,
    centroCostoNombre: null,
    requisicionId: null,
    lineaRequisicionId: null,
    requisicionFolio: null,
    fechaEntregaLinea: null,
    cantidadRecibida: 0,
    cantidadFacturada: 0,
    textoAdicional: null,
    ...overrides,
  } as unknown as LineaOrdenCompraResponse;
}

function makeOc(
  lineas: LineaOrdenCompraResponse[],
): OrdenCompraDetalleResponse {
  return {
    id: 'oc-1',
    estado: EstadoOrdenCompra.Borrador,
    sinRequisicionPrevia: true,
    lineas,
  } as unknown as OrdenCompraDetalleResponse;
}

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/catalogos/articulos', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 1000, total: 0 }),
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

describe('<EditorLineas> (OC) — columna CC-Máquina (Fase E PR3)', () => {
  it('encabezado CC-Máquina presente', () => {
    render(<EditorLineas oc={makeOc([makeLinea()])} />, {
      wrapper: createQueryWrapper(),
    });
    expect(
      screen.getByRole('columnheader', { name: /CC-Máquina/i }),
    ).toBeInTheDocument();
  });

  it('CC resoluble → "clave — nombre"', () => {
    render(
      <EditorLineas
        oc={makeOc([
          makeLinea({
            centroCostoId: 'm-1',
            centroCostoClave: 'MCLC101',
            centroCostoNombre: 'Gantry',
          }),
        ])}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByText('MCLC101 — Gantry')).toBeInTheDocument();
  });

  it('CC irresoluble (id sin fila) → "No catalogado"', () => {
    render(
      <EditorLineas
        oc={makeOc([
          makeLinea({
            centroCostoId: 'm-roto',
            centroCostoClave: null,
            centroCostoNombre: null,
          }),
        ])}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(screen.getByText('No catalogado')).toBeInTheDocument();
  });

  it('línea sin CC → "—"', () => {
    render(<EditorLineas oc={makeOc([makeLinea()])} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByText('—')).toBeInTheDocument();
  });
});
