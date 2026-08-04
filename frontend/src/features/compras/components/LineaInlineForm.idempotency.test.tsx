import { beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { LineaInlineForm } from '@/features/compras/components/LineaInlineForm';
import { useAuthStore } from '@/lib/auth/auth-store';
import { Naturaleza, EstatusCatalogo } from '@/features/compras/api/types';

/**
 * Regresión — Idempotency-Key por submit (ADR-0020).
 *
 * El inline form de "agregar línea" es multi-submit: al guardar resetea y
 * queda abierto para capturar la siguiente. Antes tomaba una sola key por
 * montaje (useFormIdempotencyKey), así que la 2ª línea reusaba la misma key
 * con un body distinto y el backend la rechazaba con 422
 * IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY. El fix genera una key fresca
 * por submit. Este test falla contra el código viejo (dos keys iguales) y
 * pasa con el fix (dos keys distintas).
 */

// Id con forma de UUID (el schema valida articuloId contra el regex UUID).
const ART_ID = '11111111-1111-1111-1111-111111111111';

beforeEach(() => {
  // Catálogo mínimo para que monte el <ArticuloSelector>.
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
    // Fase E PR2.1: prellenado del CC. Exactamente 1 Dim3 en alcance → el form
    // autocompleta centroCostoId, así el submit pasa la validación obligatoria.
    http.get('*/api/v1/centros-costo/dim3/buscar', () =>
      HttpResponse.json([
        {
          id: '0c000000-0000-0000-0000-000000000001',
          clave: 'MCLC101',
          nombre: 'Gantry',
          grupoDim3Nombre: 'Corte',
          dim2Clave: 'D2',
          dim2Nombre: 'Producción',
          dim1Clave: 'D1',
          dim1Nombre: 'Planta',
          estatus: 0,
        },
      ]),
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

async function seleccionarArticulo() {
  fireEvent.click(
    screen.getByRole('combobox', { name: /seleccionar artículo/i }),
  );
  fireEvent.click(await screen.findByText('Solvente industrial'));
}

function umInput(): HTMLInputElement {
  return screen.getByLabelText(
    /unidad de medida \(heredada del artículo\)/i,
  ) as HTMLInputElement;
}

function fijarPrecio(valor: string) {
  const moneda = screen.getByRole('combobox', { name: /moneda/i });
  const precioInput = moneda.parentElement?.querySelector(
    'input[type="number"]',
  ) as HTMLInputElement;
  fireEvent.change(precioInput, { target: { value: valor } });
}

describe('<LineaInlineForm> — Idempotency-Key por submit (regresión)', () => {
  it('dos adds seguidos envían Idempotency-Keys distintas', async () => {
    const keys: (string | null)[] = [];
    mswServer.use(
      http.post(
        '*/api/v1/compras/requisiciones/rq-1/lineas',
        async ({ request }) => {
          keys.push(request.headers.get('Idempotency-Key'));
          const n = keys.length;
          return HttpResponse.json(
            { id: `l-${n}`, requisicionId: 'rq-1', posicion: n, version: 1 },
            { status: 201 },
          );
        },
      ),
    );

    render(<LineaInlineForm requisicionId="rq-1" onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    // Add #1 (precio > 0 lo exige el schema).
    await seleccionarArticulo();
    await waitFor(() => expect(umInput().value).toBe('LT'));
    fijarPrecio('100');
    fireEvent.click(screen.getByRole('button', { name: /agregar línea/i }));
    await waitFor(() => expect(keys).toHaveLength(1));

    // El form se resetea y queda abierto → esperamos el reset antes del 2º.
    await waitFor(() => expect(umInput().value).toBe(''));

    // Add #2 con body distinto (otro precio).
    await seleccionarArticulo();
    await waitFor(() => expect(umInput().value).toBe('LT'));
    fijarPrecio('200');
    fireEvent.click(screen.getByRole('button', { name: /agregar línea/i }));
    await waitFor(() => expect(keys).toHaveLength(2));

    expect(keys[0]).toBeTruthy();
    expect(keys[1]).toBeTruthy();
    expect(keys[0]).not.toBe(keys[1]);
  });
});
