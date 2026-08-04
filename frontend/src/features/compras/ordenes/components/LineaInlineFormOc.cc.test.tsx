import { beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { LineaInlineFormOc } from '@/features/compras/ordenes/components/LineaInlineFormOc';
import { useAuthStore } from '@/lib/auth/auth-store';
import type {
  LineaOrdenCompraResponse,
  OrdenCompraDetalleResponse,
} from '@/features/compras/ordenes/api/types';

/**
 * Fase E PR3 — CC-Máquina (Dim3) en la línea de OC. El campo es CONDICIONAL
 * (ADR-0050): línea MANUAL → picker ABIERTO por proxy (el comprador elige);
 * línea HEREDADA de RQ → display BLOQUEADO read-only (1:1 de la RQ, no editable).
 */

const OC = {
  id: 'oc-1',
} as unknown as OrdenCompraDetalleResponse;

function lineaHeredada(
  overrides: Partial<LineaOrdenCompraResponse> = {},
): LineaOrdenCompraResponse {
  return {
    id: 'l-1',
    posicion: 1,
    requisicionId: 'rq-1',
    articuloId: '22222222-2222-2222-2222-222222222222',
    cantidad: 5,
    unidadMedida: 'LT',
    precioUnitario: 100,
    departamentoSolicitanteId: '44444444-4444-4444-4444-444444444444',
    descripcionExtendida: null,
    fechaEntregaLinea: null,
    textoAdicional: null,
    centroCostoId: null,
    centroCostoClave: null,
    centroCostoNombre: null,
    ...overrides,
  } as unknown as LineaOrdenCompraResponse;
}

const ccInput = () =>
  screen.getByLabelText(
    /CC-Máquina \(heredado de la requisición, no editable\)/i,
  ) as HTMLInputElement;

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/catalogos/articulos', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 50, total: 0 }),
    ),
    // El picker solo consulta con el popover abierto; el mount no lo dispara,
    // pero lo dejamos por robustez si algún día se prellena.
    http.get('*/api/v1/compras/ordenes/dim3/buscar', () =>
      HttpResponse.json([]),
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

describe('<LineaInlineFormOc> — CC-Máquina condicional (Fase E PR3)', () => {
  it('línea manual (modo agregar): muestra el picker ABIERTO, no el display bloqueado', () => {
    render(<LineaInlineFormOc oc={OC} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    // Combobox del picker (captura por proxy). Siempre visible, fuera de "Detalles".
    expect(
      screen.getByRole('combobox', { name: /seleccionar máquina/i }),
    ).toBeInTheDocument();
    // No hay input read-only de "heredado" en una línea manual.
    expect(
      screen.queryByLabelText(
        /CC-Máquina \(heredado de la requisición, no editable\)/i,
      ),
    ).not.toBeInTheDocument();
  });

  it('línea heredada con CC: display read-only "clave — nombre", sin picker', () => {
    render(
      <LineaInlineFormOc
        oc={OC}
        linea={lineaHeredada({
          centroCostoId: 'm-1',
          centroCostoClave: 'MCLC101',
          centroCostoNombre: 'Gantry',
        })}
        onCancel={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(ccInput().value).toBe('MCLC101 — Gantry');
    expect(ccInput()).toHaveAttribute('readonly');
    // Bloqueado: no se ofrece el combobox de selección.
    expect(
      screen.queryByRole('combobox', { name: /seleccionar máquina/i }),
    ).not.toBeInTheDocument();
  });

  // ─── Fase E PR3.1: obligatoriedad en la línea MANUAL ───

  it('línea manual legada sin CC: bloquea el submit y muestra el error inline', async () => {
    const patches: string[] = [];
    mswServer.use(
      http.patch('*/api/v1/compras/ordenes/oc-1/lineas/l-1', async () => {
        patches.push('patch');
        return HttpResponse.json({}, { status: 200 });
      }),
    );

    render(
      <LineaInlineFormOc
        oc={OC}
        linea={lineaHeredada({ requisicionId: null, centroCostoId: null })}
        onCancel={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );

    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    expect(await screen.findByText(/CC-Máquina requerido/i)).toBeInTheDocument();
    expect(patches).toHaveLength(0);
  });

  it('línea HEREDADA sin CC: NO la bloquea la obligatoriedad de PR3.1', async () => {
    // El campo es read-only en heredadas: exigirlo aquí dejaría la línea
    // imposible de guardar. Su CC se arregla en la RQ, que ya lo obliga.
    const patches: string[] = [];
    mswServer.use(
      http.patch('*/api/v1/compras/ordenes/oc-1/lineas/l-1', async () => {
        patches.push('patch');
        return HttpResponse.json({}, { status: 200 });
      }),
    );

    render(
      <LineaInlineFormOc
        oc={OC}
        linea={lineaHeredada()}
        onCancel={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );

    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() => expect(patches).toHaveLength(1));
  });

  it('línea heredada sin CC: display read-only "—"', () => {
    render(
      <LineaInlineFormOc
        oc={OC}
        linea={lineaHeredada()}
        onCancel={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(ccInput().value).toBe('—');
    expect(ccInput()).toHaveAttribute('readonly');
  });
});
