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

/** F1-MP-01 (E1): medida de hoja en la línea de OC de vidrio crudo. */

const ART_VIDRIO = '22222222-2222-2222-2222-222222222222';
const ART_PIEZA = '33333333-3333-3333-3333-333333333333';
const VAR_A = 'aaaaaaaa-0000-0000-0000-00000000000a';
const VAR_B = 'bbbbbbbb-0000-0000-0000-00000000000b';

const base = {
  id: 'l-1',
  posicion: 1,
  requisicionId: null,
  cantidad: 300,
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
const LINEA_VIDRIO = { ...base, articuloId: ART_VIDRIO } as unknown as LineaOrdenCompraResponse;
const LINEA_PIEZA = { ...base, articuloId: ART_PIEZA, unidadMedida: 'PZA' } as unknown as LineaOrdenCompraResponse;
const OC = { id: 'oc-1', esImportacion: false, estado: EstadoOrdenCompra.Borrador } as unknown as OrdenCompraDetalleResponse;

beforeEach(() => {
  mswServer.use(
    http.get(`*/api/v1/catalogos/articulos/${ART_VIDRIO}/medidas`, () =>
      HttpResponse.json([
        { varianteId: VAR_A, claveVariante: 'A', anchoMm: 2440, altoMm: 3660, espesorMm: 6, areaHojaM2: 8.9304 },
        { varianteId: VAR_B, claveVariante: 'B', anchoMm: 2140, altoMm: 3300, espesorMm: 6, areaHojaM2: 7.062 },
      ]),
    ),
    http.get(`*/api/v1/catalogos/articulos/${ART_PIEZA}/medidas`, () => HttpResponse.json([])),
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

// jsdom no trae pointer capture, que usa el Select de Radix.
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.releasePointerCapture ??= () => {};

async function elegirMedida(texto: string) {
  const trigger = await screen.findByRole('combobox', { name: /medida/i });
  fireEvent.keyDown(trigger, { key: 'ArrowDown' });
  const opcion = await screen.findByRole('option', { name: texto });
  fireEvent.keyDown(opcion, { key: 'Enter' });
}

describe('<LineaInlineFormOc> — medida de hoja', () => {
  it('elige medida + 20 hojas → 178.6080 m² y el PATCH lleva la variante y la cantidad', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.patch('*/api/v1/compras/ordenes/oc-1/lineas/l-1', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    render(<LineaInlineFormOc oc={OC} linea={LINEA_VIDRIO} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    await elegirMedida('2440 × 3660 mm');
    fireEvent.change(screen.getByLabelText(/^Hojas/), { target: { value: '20' } });
    expect(screen.getByText('178.6080')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));
    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ productoAwVarianteId: VAR_A, cantidad: 178.608, unidadMedida: 'M2' });
  });

  it('sin medida muestra «Elige la medida de la hoja.» y no envía', async () => {
    let enviado = false;
    mswServer.use(
      http.patch('*/api/v1/compras/ordenes/oc-1/lineas/l-1', () => {
        enviado = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    render(<LineaInlineFormOc oc={OC} linea={LINEA_VIDRIO} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    await screen.findByRole('combobox', { name: /medida/i });
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));
    expect(await screen.findByText('Elige la medida de la hoja.')).toBeInTheDocument();
    expect(enviado).toBe(false);
  });

  it('un artículo que no es de vidrio no tiene campo de medida', async () => {
    render(<LineaInlineFormOc oc={OC} linea={LINEA_PIEZA} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    await screen.findByLabelText(/^Cantidad/);
    expect(screen.queryByRole('combobox', { name: /medida/i })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/^Hojas/)).not.toBeInTheDocument();
  });

  it('el 422 del servidor se muestra en el campo Medida', async () => {
    mswServer.use(
      http.patch('*/api/v1/compras/ordenes/oc-1/lineas/l-1', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Regla de negocio',
            status: 422,
            code: 'VARIANTE_NO_PERTENECE_AL_ARTICULO',
            detail: 'La medida no pertenece al artículo.',
          },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<LineaInlineFormOc oc={OC} linea={LINEA_VIDRIO} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    await elegirMedida('2140 × 3300 mm');
    fireEvent.change(screen.getByLabelText(/^Hojas/), { target: { value: '15' } });
    expect(screen.getByText('105.9300')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));
    expect(await screen.findByText('La medida no pertenece al artículo.')).toBeInTheDocument();
  });
});
