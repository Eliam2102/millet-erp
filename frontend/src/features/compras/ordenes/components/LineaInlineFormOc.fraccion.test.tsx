import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { toast } from 'sonner';
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

/**
 * Fracción arancelaria por partida (supuesto Vilo, F1-COM-06). Solo visible y
 * editable al EDITAR una partida de OC de comercio exterior; valida 8 o 10
 * dígitos y muestra el error del API junto al campo.
 */

const ART_ID = '22222222-2222-2222-2222-222222222222';
const DEP_ID = '44444444-4444-4444-4444-444444444444';

const LINEA = {
  id: 'l-1',
  posicion: 1,
  requisicionId: null,
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
} as unknown as LineaOrdenCompraResponse;

function ocConModo(esImportacion: boolean) {
  return {
    id: 'oc-1',
    esImportacion,
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

describe('<LineaInlineFormOc> — fracción arancelaria', () => {
  it('no muestra el campo si la OC no es de comercio exterior', () => {
    render(<LineaInlineFormOc oc={ocConModo(false)} linea={LINEA} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    expect(screen.queryByLabelText('Fracción arancelaria')).not.toBeInTheDocument();
  });

  it('valida 8 u 10 dígitos y no envía el PATCH si la fracción es inválida', async () => {
    let patchCount = 0;
    mswServer.use(
      http.patch('*/api/v1/compras/ordenes/oc-1/lineas/l-1', () => {
        patchCount += 1;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    render(<LineaInlineFormOc oc={ocConModo(true)} linea={LINEA} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    fireEvent.change(screen.getByLabelText('Fracción arancelaria'), {
      target: { value: '7007199' },
    });
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    expect(
      await screen.findByText('La fracción arancelaria debe tener 8 o 10 dígitos.'),
    ).toBeInTheDocument();
    expect(patchCount).toBe(0);
  });

  it('envía la fracción de 10 dígitos en el PATCH de la partida', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.patch('*/api/v1/compras/ordenes/oc-1/lineas/l-1', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    render(<LineaInlineFormOc oc={ocConModo(true)} linea={LINEA} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    fireEvent.change(screen.getByLabelText('Fracción arancelaria'), {
      target: { value: '7007199999' },
    });
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ fraccionArancelaria: '7007199999' });
    expect(body).not.toHaveProperty('limpiarFraccionArancelaria');
  });

  it('muestra junto al campo el error del API (400 FRACCION_ARANCELARIA_INVALIDA) y el toast con el detalle', async () => {
    mswServer.use(
      http.patch('*/api/v1/compras/ordenes/oc-1/lineas/l-1', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Solicitud inválida',
            status: 400,
            code: 'FRACCION_ARANCELARIA_INVALIDA',
            detail: 'La fracción arancelaria debe tener 8 o 10 dígitos.',
          },
          { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    const errorSpy = vi.spyOn(toast, 'error');

    render(<LineaInlineFormOc oc={ocConModo(true)} linea={LINEA} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    fireEvent.change(screen.getByLabelText('Fracción arancelaria'), {
      target: { value: '70071999' },
    });
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() =>
      expect(
        screen.getByText('La fracción arancelaria debe tener 8 o 10 dígitos.', {
          selector: 'p',
        }),
      ).toBeInTheDocument(),
    );
    expect(errorSpy).toHaveBeenCalledWith(
      'Solicitud inválida',
      expect.objectContaining({
        description: expect.stringContaining('La fracción arancelaria debe tener 8 o 10 dígitos.'),
      }),
    );
    errorSpy.mockRestore();
  });

  it('limpia la fracción previa cuando el campo queda vacío', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.patch('*/api/v1/compras/ordenes/oc-1/lineas/l-1', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    render(
      <LineaInlineFormOc
        oc={ocConModo(true)}
        linea={{ ...LINEA, fraccionArancelaria: '70071999' } as LineaOrdenCompraResponse}
        onCancel={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );

    fireEvent.change(screen.getByLabelText('Fracción arancelaria'), { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ limpiarFraccionArancelaria: true });
    expect(body).not.toHaveProperty('fraccionArancelaria');
  });
});
