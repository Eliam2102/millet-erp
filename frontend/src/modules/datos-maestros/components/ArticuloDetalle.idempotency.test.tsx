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
import { ArticuloDetalle } from '@/modules/datos-maestros/components/ArticuloDetalle';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Regresión — Idempotency-Key por acción en "Desactivar" (ADR-0020, Bug B).
 *
 * El detalle queda montado al navegar entre artículos. Antes tomaba una key
 * estable por montaje (useFormIdempotencyKey); como el llaveo de idempotencia
 * NO incluye el path y el DELETE no lleva body, desactivar un 2º artículo en la
 * misma sesión reentraba (empresa, usuario, key) con el mismo hash → el backend
 * replicaba el 204 cacheado del 1º SIN ejecutar el 2º (no-op silencioso). El fix
 * genera una key fresca por acción.
 *
 * Proxy fiel a la raíz ("key por montaje vs por acción"): dos desactivaciones
 * consecutivas en el mismo detalle montado deben enviar keys distintas. Pre-fix:
 * misma key. Post-fix: distintas.
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
  useParams: () => ({ id: 'a-1' }),
}));

const ARTICULO = {
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
  // El detalle siempre responde Activo, así que el botón Desactivar sigue
  // visible tras el primer DELETE (permite la 2ª acción). El form anidado monta
  // el <UnidadMedidaSelect> → lista vacía basta.
  mswServer.use(
    http.get('*/api/v1/datos-maestros/articulos/a-1', () =>
      HttpResponse.json(ARTICULO),
    ),
    http.get('*/api/v1/catalogos/unidades-medida', () => HttpResponse.json([])),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [
      PermisosCanonicos.DatosMaestrosArticulosGestionar,
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

describe('<ArticuloDetalle> — Idempotency-Key por acción (regresión Bug B)', () => {
  it('dos desactivaciones consecutivas envían Idempotency-Keys distintas', async () => {
    const keys: (string | null)[] = [];
    mswServer.use(
      http.delete('*/api/v1/catalogos/articulos/a-1', ({ request }) => {
        keys.push(request.headers.get('Idempotency-Key'));
        return new HttpResponse(null, { status: 204 });
      }),
    );

    render(<ArticuloDetalle />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('A-001')).toBeInTheDocument());

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
