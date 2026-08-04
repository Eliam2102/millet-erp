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
 * Regresión — Idempotency-Key por submit (ADR-0020), contraparte de OC.
 *
 * El inline form de líneas de OC también es multi-submit y reusaba una sola
 * key por montaje (useFormIdempotencyKey). El fix genera la key al tope de
 * onSubmit, donde la consumen AMBAS ramas (editar y agregar). Se ejercita la
 * rama EDITAR porque deja el form pre-válido sin conducir los dos selectores
 * async que exige el alta manual (artículo + departamento) — es exactamente
 * la misma línea del fix. Dos submits → dos keys distintas (con el bug viejo
 * eran iguales).
 */

// Ids con forma de UUID (el schema valida los *Id contra el regex UUID).
const ART_ID = '22222222-2222-2222-2222-222222222222';
const DEP_ID = '44444444-4444-4444-4444-444444444444';

const OC = {
  id: 'oc-1',
} as unknown as OrdenCompraDetalleResponse;

// Línea manual (sin requisicionId) → editar manda los campos reales y el
// form arranca pre-válido desde buildValuesFromLinea.
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
  // Fase E PR3.1: el CC-Máquina es requerido en la línea manual, así que el
  // form no dejaría hacer submit sin él (este test va de Idempotency-Key).
  centroCostoId: '0c000000-0000-0000-0000-000000000001',
  centroCostoClave: 'MCLC101',
  centroCostoNombre: 'Gantry',
} as unknown as LineaOrdenCompraResponse;

beforeEach(() => {
  // El <ArticuloSelector> de la fila crítica monta y consulta el catálogo.
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

describe('<LineaInlineFormOc> — Idempotency-Key por submit (regresión)', () => {
  it('dos submits seguidos envían Idempotency-Keys distintas', async () => {
    const keys: (string | null)[] = [];
    mswServer.use(
      http.patch(
        '*/api/v1/compras/ordenes/oc-1/lineas/l-1',
        async ({ request }) => {
          keys.push(request.headers.get('Idempotency-Key'));
          return HttpResponse.json({}, { status: 200 });
        },
      ),
    );

    render(<LineaInlineFormOc oc={OC} linea={LINEA} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    const guardar = () =>
      screen.getByRole('button', { name: /guardar cambios/i });

    fireEvent.click(guardar());
    await waitFor(() => expect(keys).toHaveLength(1));
    await waitFor(() => expect(guardar()).not.toBeDisabled());

    fireEvent.click(guardar());
    await waitFor(() => expect(keys).toHaveLength(2));

    expect(keys[0]).toBeTruthy();
    expect(keys[1]).toBeTruthy();
    expect(keys[0]).not.toBe(keys[1]);
  });
});
