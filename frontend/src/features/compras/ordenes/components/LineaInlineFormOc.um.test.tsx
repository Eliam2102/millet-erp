import { beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { LineaInlineFormOc } from '@/features/compras/ordenes/components/LineaInlineFormOc';
import { useAuthStore } from '@/lib/auth/auth-store';
import { Naturaleza, EstatusCatalogo } from '@/features/compras/api/types';
import type {
  LineaOrdenCompraResponse,
  OrdenCompraDetalleResponse,
} from '@/features/compras/ordenes/api/types';

/**
 * Bug 2 (OC) — la UM de la línea de OC se hereda del artículo en un campo
 * read-only. En líneas heredadas de una RQ el artículo está bloqueado, así
 * que la UM queda fija con la unidad guardada en la RQ.
 */

const ART_ID = '22222222-2222-2222-2222-222222222222';

const OC = {
  id: 'oc-1',
} as unknown as OrdenCompraDetalleResponse;

function mockCatalogo(unidadMedidaDefault: string) {
  mswServer.use(
    http.get('*/api/v1/catalogos/articulos', () =>
      HttpResponse.json({
        items: [
          {
            id: ART_ID,
            clave: 'SOLV-001',
            nombre: 'Solvente industrial',
            unidadMedidaDefault,
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
}

beforeEach(() => {
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

const um = () =>
  screen.getByLabelText(
    /unidad de medida \(heredada del artículo\)/i,
  ) as HTMLInputElement;

describe('<LineaInlineFormOc> — herencia de unidad de medida (Bug 2)', () => {
  it('línea manual: al seleccionar artículo, la UM se puebla y es read-only', async () => {
    mockCatalogo('LT');
    render(<LineaInlineFormOc oc={OC} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    expect(um().value).toBe('');
    expect(um()).toHaveAttribute('readonly');
    expect(um()).not.toBeDisabled();

    fireEvent.click(
      screen.getByRole('combobox', { name: /seleccionar artículo/i }),
    );
    fireEvent.click(await screen.findByText('Solvente industrial'));

    await waitFor(() => expect(um().value).toBe('LT'));
  });

  it('línea heredada de RQ: UM fija con la unidad de la RQ y artículo bloqueado', () => {
    mockCatalogo('KG'); // catálogo distinto a la UM guardada: no debe re-derivar
    const linea = {
      id: 'l-1',
      posicion: 1,
      requisicionId: 'rq-1',
      articuloId: ART_ID,
      cantidad: 5,
      unidadMedida: 'LT',
      precioUnitario: 100,
      departamentoSolicitanteId: 'dep-1',
      descripcionExtendida: null,
      fechaEntregaLinea: null,
      textoAdicional: null,
    } as unknown as LineaOrdenCompraResponse;

    render(<LineaInlineFormOc oc={OC} linea={linea} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    // La UM viene de la línea de RQ ('LT'), read-only, no del catálogo ('KG').
    expect(um().value).toBe('LT');
    expect(um()).toHaveAttribute('readonly');
    // El selector de artículo está bloqueado (la RQ es la fuente de verdad).
    expect(
      screen.getByRole('combobox', { name: /seleccionar artículo/i }),
    ).toBeDisabled();
  });
});
