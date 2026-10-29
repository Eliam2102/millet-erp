import { beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { LineaInlineFormOc } from '@/features/compras/ordenes/components/LineaInlineFormOc';
import { EditorLineas } from '@/features/compras/ordenes/components/EditorLineas';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  EstadoOrdenCompra,
  type LineaOrdenCompraResponse,
  type OrdenCompraDetalleResponse,
} from '@/features/compras/ordenes/api/types';

/** F1-MP-05: lote del proveedor en la línea de OC (vidrio y demás líneas que no son servicio). */

const ART_VIDRIO = '22222222-2222-2222-2222-222222222222';
const ART_PIEZA = '33333333-3333-3333-3333-333333333333';
const VAR_A = 'aaaaaaaa-0000-0000-0000-00000000000a';

const base = {
  id: 'l-1',
  posicion: 1,
  requisicionId: null,
  cantidad: 8.9304,
  unidadMedida: 'M2',
  precioUnitario: 100,
  departamentoSolicitanteId: '44444444-4444-4444-4444-444444444444',
  descripcionExtendida: null,
  fechaEntregaLinea: null,
  textoAdicional: null,
  centroCostoId: '0c000000-0000-0000-0000-000000000001',
  centroCostoClave: 'MCLC101',
  centroCostoNombre: 'Gantry',
  fraccionArancelaria: null,
};
const LINEA_VIDRIO = {
  ...base,
  articuloId: ART_VIDRIO,
  productoAwVarianteId: VAR_A,
  areaHojaM2: 8.9304,
} as unknown as LineaOrdenCompraResponse;
const LINEA_PIEZA = {
  ...base,
  articuloId: ART_PIEZA,
  unidadMedida: 'PZA',
  cantidad: 5,
} as unknown as LineaOrdenCompraResponse;
const LINEA_SERVICIO = { ...LINEA_PIEZA, esServicio: true } as unknown as LineaOrdenCompraResponse;
const OC = { id: 'oc-1', esImportacion: false, estado: EstadoOrdenCompra.Borrador } as unknown as OrdenCompraDetalleResponse;

beforeEach(() => {
  mswServer.use(
    http.get(`*/api/v1/catalogos/articulos/${ART_VIDRIO}/medidas`, () =>
      HttpResponse.json([
        { varianteId: VAR_A, claveVariante: 'A', anchoMm: 2440, altoMm: 3660, espesorMm: 6, areaHojaM2: 8.9304 },
      ]),
    ),
    http.get(`*/api/v1/catalogos/articulos/${ART_PIEZA}/medidas`, () => HttpResponse.json([])),
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

function capturarPatch() {
  const captura: { body: Record<string, unknown> | null } = { body: null };
  mswServer.use(
    http.patch('*/api/v1/compras/ordenes/oc-1/lineas/l-1', async ({ request }) => {
      captura.body = (await request.json()) as Record<string, unknown>;
      return new HttpResponse(null, { status: 204 });
    }),
  );
  return captura;
}

function pintar(linea: LineaOrdenCompraResponse) {
  render(<LineaInlineFormOc oc={OC} linea={linea} onCancel={() => {}} />, {
    wrapper: createQueryWrapper(),
  });
}

describe('<LineaInlineFormOc> — lote del proveedor', () => {
  it('se muestra en vidrio y en otra línea de material, y no en servicio', async () => {
    pintar(LINEA_VIDRIO);
    expect(await screen.findByLabelText('Lote del proveedor (opcional)')).toBeInTheDocument();
  });

  it('línea de pieza (sin medidas) también lo muestra', async () => {
    pintar(LINEA_PIEZA);
    expect(await screen.findByLabelText('Lote del proveedor (opcional)')).toBeInTheDocument();
  });

  it('línea de servicio no lo muestra', async () => {
    pintar(LINEA_SERVICIO);
    await screen.findByLabelText(/^Cantidad/);
    expect(screen.queryByLabelText('Lote del proveedor (opcional)')).not.toBeInTheDocument();
  });

  it('envía el lote sin espacios en el PATCH', async () => {
    const captura = capturarPatch();
    pintar(LINEA_PIEZA);
    fireEvent.change(await screen.findByLabelText('Lote del proveedor (opcional)'), {
      target: { value: '  L-7781 ' },
    });
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));
    await waitFor(() => expect(captura.body).not.toBeNull());
    expect(captura.body).toMatchObject({ loteProveedor: 'L-7781' });
    expect(captura.body).not.toHaveProperty('limpiarLoteProveedor');
  });

  it('vaciar un lote previo envía limpiarLoteProveedor', async () => {
    const captura = capturarPatch();
    pintar({ ...LINEA_PIEZA, loteProveedor: 'L-7781' } as LineaOrdenCompraResponse);
    const campo = await screen.findByLabelText('Lote del proveedor (opcional)');
    expect(campo).toHaveValue('L-7781');
    fireEvent.change(campo, { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));
    await waitFor(() => expect(captura.body).not.toBeNull());
    expect(captura.body).toMatchObject({ limpiarLoteProveedor: true });
    expect(captura.body).not.toHaveProperty('loteProveedor');
  });

  it('sin lote previo ni captura no manda nada del lote', async () => {
    const captura = capturarPatch();
    pintar(LINEA_PIEZA);
    await screen.findByLabelText('Lote del proveedor (opcional)');
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));
    await waitFor(() => expect(captura.body).not.toBeNull());
    expect(captura.body).not.toHaveProperty('loteProveedor');
    expect(captura.body).not.toHaveProperty('limpiarLoteProveedor');
  });

  it('51 caracteres: error en el campo y no se envía', async () => {
    const captura = capturarPatch();
    pintar(LINEA_PIEZA);
    fireEvent.change(await screen.findByLabelText('Lote del proveedor (opcional)'), {
      target: { value: 'L'.repeat(51) },
    });
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));
    expect(await screen.findByText('Máximo 50 caracteres.')).toBeInTheDocument();
    expect(captura.body).toBeNull();
  });

  it('LINEA_OC_LOTE_LARGO del API se pinta en el campo', async () => {
    mswServer.use(
      http.patch('*/api/v1/compras/ordenes/oc-1/lineas/l-1', () =>
        HttpResponse.json(
          { title: 'Lote inválido', detail: 'El lote del proveedor excede 50 caracteres.', status: 422, code: 'LINEA_OC_LOTE_LARGO' },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    pintar(LINEA_PIEZA);
    fireEvent.change(await screen.findByLabelText('Lote del proveedor (opcional)'), {
      target: { value: 'L-1' },
    });
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));
    expect(await screen.findByText('El lote del proveedor excede 50 caracteres.', { selector: 'p' })).toBeInTheDocument();
  });
});

describe('<EditorLineas> — «Lote X» bajo la medida', () => {
  const ocCon = (l: LineaOrdenCompraResponse) =>
    ({ id: 'oc-1', moneda: 'MXN', estado: EstadoOrdenCompra.Borrador, sinRequisicionPrevia: true, lineas: [l] }) as unknown as OrdenCompraDetalleResponse;

  it('con lote lo muestra; sin lote no hay nada', () => {
    const { unmount } = render(<EditorLineas oc={ocCon({ ...LINEA_VIDRIO, loteProveedor: 'L-7781' } as LineaOrdenCompraResponse)} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.getByTestId('oc-linea-lote')).toHaveTextContent('Lote L-7781');
    unmount();
    render(<EditorLineas oc={ocCon(LINEA_VIDRIO)} />, { wrapper: createQueryWrapper() });
    expect(screen.queryByTestId('oc-linea-lote')).not.toBeInTheDocument();
  });
});
