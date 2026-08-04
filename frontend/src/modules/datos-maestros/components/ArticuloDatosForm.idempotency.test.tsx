import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ArticuloDatosForm } from '@/modules/datos-maestros/components/ArticuloDatosForm';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { ArticuloDetalle } from '@/modules/datos-maestros/api/types';

/**
 * Regresión — Idempotency-Key por submit (ADR-0020, Bug B).
 *
 * El form de edición de artículo es multi-submit: tras guardar se resetea y
 * queda montado para seguir editando. Antes tomaba una sola key por montaje
 * (useFormIdempotencyKey), así que el 2º guardado del mismo artículo reusaba la
 * key con un body distinto y el backend respondía 422
 * IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY. El fix genera una key fresca por
 * submit. Este test falla contra el código viejo (misma key) y pasa con el fix.
 */

const ARTICULO: ArticuloDetalle = {
  id: 'a-1',
  clave: 'A-001',
  claveLegacy: null,
  nombre: 'Caja de cartón',
  descripcionLarga: null,
  unidadMedidaDefault: 'PZA',
  unidadMedidaId: null,
  naturaleza: 0,
  categoria: null,
  precioReferenciaMonto: null,
  precioReferenciaMoneda: null,
  estatus: 0,
};

beforeEach(() => {
  // El <UnidadMedidaSelect> dispara su query al montar; lista vacía basta.
  mswServer.use(
    http.get('*/api/v1/catalogos/unidades-medida', () => HttpResponse.json([])),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.CompartidoCatalogosAdministrar],
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

describe('<ArticuloDatosForm> — Idempotency-Key por submit (regresión Bug B)', () => {
  it('dos guardados del mismo artículo envían Idempotency-Keys distintas', async () => {
    const keys: (string | null)[] = [];
    mswServer.use(
      http.patch('*/api/v1/catalogos/articulos/a-1', async ({ request }) => {
        keys.push(request.headers.get('Idempotency-Key'));
        return new HttpResponse(null, { status: 204 });
      }),
    );

    render(<ArticuloDatosForm articulo={ARTICULO} />, {
      wrapper: createQueryWrapper(),
    });

    const nombre = screen.getByDisplayValue('Caja de cartón') as HTMLInputElement;
    const guardar = () =>
      screen.getByRole('button', { name: /guardar cambios/i });

    // Guardado #1: modifica el nombre → dirty → submit.
    fireEvent.change(nombre, { target: { value: 'Caja grande' } });
    fireEvent.click(guardar());
    await waitFor(() => expect(keys).toHaveLength(1));

    // El form se resetea y queda montado. 2º cambio con body distinto.
    fireEvent.change(nombre, { target: { value: 'Caja chica' } });
    await waitFor(() => expect(guardar()).toBeEnabled());
    fireEvent.click(guardar());
    await waitFor(() => expect(keys).toHaveLength(2));

    expect(keys[0]).toBeTruthy();
    expect(keys[1]).toBeTruthy();
    expect(keys[0]).not.toBe(keys[1]);
  });
});
