import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ProveedorDetalle } from '@/modules/datos-maestros/components/ProveedorDetalle';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Regresión — Idempotency-Key por acción en "Desactivar" (ADR-0020, Bug B).
 * Espejo del defecto de artículo: desactivar un 2º proveedor en la misma sesión
 * reusaba la key estable del montaje → replay del 204 cacheado del 1º sin
 * ejecutar el 2º (no-op silencioso). El fix genera una key fresca por acción.
 *
 * Proxy fiel a la raíz: dos desactivaciones consecutivas en el mismo detalle
 * montado deben enviar keys distintas.
 */

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    ...rest
  }: {
    children: React.ReactNode;
    to: string;
    [key: string]: unknown;
  }) => (
    <a href={to} {...rest}>
      {children}
    </a>
  ),
  useParams: () => ({ id: 'p-1' }),
}));

const PROVEEDOR = {
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
  mswServer.use(
    http.get('*/api/v1/datos-maestros/proveedores/p-1', () =>
      HttpResponse.json(PROVEEDOR),
    ),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [
      PermisosCanonicos.DatosMaestrosProveedoresGestionar,
      PermisosCanonicos.CompartidoCatalogosAdministrar,
    ],
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

async function desactivarUnaVez() {
  fireEvent.click(screen.getByRole('button', { name: /^desactivar$/i }));
  const dialog = await screen.findByRole('alertdialog');
  fireEvent.click(within(dialog).getByRole('button', { name: /^desactivar$/i }));
}

describe('<ProveedorDetalle> — Idempotency-Key por acción (regresión Bug B)', () => {
  it('dos desactivaciones consecutivas envían Idempotency-Keys distintas', async () => {
    const keys: (string | null)[] = [];
    mswServer.use(
      http.delete('*/api/v1/catalogos/proveedores/p-1', ({ request }) => {
        keys.push(request.headers.get('Idempotency-Key'));
        return new HttpResponse(null, { status: 204 });
      }),
    );

    render(<ProveedorDetalle />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('P-001')).toBeInTheDocument());

    await desactivarUnaVez();
    await waitFor(() => expect(keys).toHaveLength(1));
    await waitFor(() =>
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument(),
    );

    await desactivarUnaVez();
    await waitFor(() => expect(keys).toHaveLength(2));

    expect(keys[0]).toBeTruthy();
    expect(keys[1]).toBeTruthy();
    expect(keys[0]).not.toBe(keys[1]);
  });
});
