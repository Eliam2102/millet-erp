import { beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { LineaInlineFormOc } from '@/features/compras/ordenes/components/LineaInlineFormOc';
import { useAuthStore } from '@/lib/auth/auth-store';
import { Naturaleza, EstatusCatalogo } from '@/features/compras/api/types';
import {
  EstadoOrdenCompra,
  type LineaOrdenCompraResponse,
  type OrdenCompraDetalleResponse,
} from '@/features/compras/ordenes/api/types';

/** Aviso «Se aparta de la cotización elegida» al editar el precio de una partida que vino de la cotización (D1, F1-COM-02). */

const ART_ID = '22222222-2222-2222-2222-222222222222';
const DEP_ID = '44444444-4444-4444-4444-444444444444';

const LINEA = {
  id: 'l-1',
  posicion: 1,
  articuloId: ART_ID,
  cantidad: 5,
  unidadMedida: 'LT',
  precioUnitario: 100,
  departamentoSolicitanteId: DEP_ID,
  descripcionExtendida: null,
  fechaEntregaLinea: null,
  textoAdicional: null,
  centroCostoId: '0c000000-0000-0000-0000-000000000001',
  centroCostoClave: 'MCLC101',
  centroCostoNombre: 'Gantry',
  fraccionArancelaria: null,
  lineaRequisicionId: 'lrq-1',
  requisicionId: 'rq-1',
} as unknown as LineaOrdenCompraResponse;

function ocConCotizacion() {
  return {
    id: 'oc-1',
    esImportacion: false,
    cotizacionElegida: { precios: [{ lineaRequisicionId: 'lrq-1', precioPorUnidadRequisicion: 100 }] },
    estado: EstadoOrdenCompra.Borrador,
  } as unknown as OrdenCompraDetalleResponse;
}

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/catalogos/articulos', () =>
      HttpResponse.json({
        items: [
          {
            id: ART_ID,
            clave: 'SOLV-001',
            nombre: 'Solvente industrial',
            unidadMedidaDefault: 'LT',
            naturaleza: Naturaleza.Estandar,
            categoria: null,
            estatus: EstatusCatalogo.Activo,
          },
        ],
        offset: 0,
        limit: 50,
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

describe('<LineaInlineFormOc> — aparta de la cotización', () => {
  it('avisa solo cuando el precio editado difiere del cotizado', async () => {
    render(<LineaInlineFormOc oc={ocConCotizacion()} linea={LINEA} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.queryByText(/Se aparta de la cotización elegida/)).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText(/^Precio/), { target: { value: '120' } });
    expect(await screen.findByRole('note')).toHaveTextContent('Se aparta de la cotización elegida');
  });
});
