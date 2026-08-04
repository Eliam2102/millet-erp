import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ProveedorDatosForm } from '@/modules/datos-maestros/components/ProveedorDatosForm';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { ProveedorDetalle } from '@/modules/datos-maestros/api/types';

/**
 * Regresión — Idempotency-Key por submit (ADR-0020, Bug B). Espejo del defecto
 * de artículo: el form de edición de proveedor es multi-submit y antes reusaba
 * una key estable por montaje → 422 al 2º guardado. El fix genera una key
 * fresca por submit.
 */

const PROVEEDOR: ProveedorDetalle = {
  id: 'p-1',
  clave: 'P-001',
  claveLegacy: null,
  razonSocial: 'Proveedor Uno SA',
  nombreComercial: null,
  rfc: 'PUN010101AAA',
  tipoPersona: 0,
  condicionesPagoDias: null,
  monedaPreferidaId: null,
  email: null,
  telefono: null,
  estatus: 0,
};

beforeEach(() => {
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

describe('<ProveedorDatosForm> — Idempotency-Key por submit (regresión Bug B)', () => {
  it('dos guardados del mismo proveedor envían Idempotency-Keys distintas', async () => {
    const keys: (string | null)[] = [];
    mswServer.use(
      http.patch('*/api/v1/catalogos/proveedores/p-1', async ({ request }) => {
        keys.push(request.headers.get('Idempotency-Key'));
        return new HttpResponse(null, { status: 204 });
      }),
    );

    render(<ProveedorDatosForm proveedor={PROVEEDOR} />, {
      wrapper: createQueryWrapper(),
    });

    const razon = screen.getByDisplayValue('Proveedor Uno SA') as HTMLInputElement;
    const guardar = () =>
      screen.getByRole('button', { name: /guardar cambios/i });

    // Guardado #1.
    fireEvent.change(razon, { target: { value: 'Proveedor Uno SA de CV' } });
    fireEvent.click(guardar());
    await waitFor(() => expect(keys).toHaveLength(1));

    // 2º cambio con body distinto (el form quedó montado).
    fireEvent.change(razon, { target: { value: 'Proveedor Uno SAPI' } });
    await waitFor(() => expect(guardar()).toBeEnabled());
    fireEvent.click(guardar());
    await waitFor(() => expect(keys).toHaveLength(2));

    expect(keys[0]).toBeTruthy();
    expect(keys[1]).toBeTruthy();
    expect(keys[0]).not.toBe(keys[1]);
  });
});
