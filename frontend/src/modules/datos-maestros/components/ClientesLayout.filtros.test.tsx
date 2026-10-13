import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ClientesLayout } from '@/modules/datos-maestros/components/ClientesLayout';
import { NuevoClienteContext } from '@/modules/datos-maestros/components/nuevo-cliente-context';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  resetClientesFiltros,
  useClientesFiltrosStore,
} from '@/modules/datos-maestros/stores/clientes-filtros-store';

/**
 * M-23 / C-2: los filtros de la bandeja de clientes viven en un store a
 * nivel de módulo, así que sobreviven al desmontar el layout (la ruta
 * <c>clientes/index</c> y <c>clientes/$id</c> montan layouts distintos).
 */
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    className,
    ...rest
  }: {
    children: React.ReactNode;
    to: string;
    className?: string;
    [key: string]: unknown;
  }) => (
    <a href={to} className={className} {...rest}>
      {children}
    </a>
  ),
}));

const NUEVO_CLIENTE_API = {
  abrir: () => {},
  cerrar: () => {},
  setDirty: () => {},
};

function renderLayout(idActivo: string | null) {
  return render(
    <NuevoClienteContext.Provider value={NUEVO_CLIENTE_API}>
      <ClientesLayout idActivo={idActivo} />
    </NuevoClienteContext.Provider>,
    { wrapper: createQueryWrapper() },
  );
}

beforeEach(() => {
  resetClientesFiltros();
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.DatosMaestrosClientesGestionar],
    errorMessage: null,
  });
  mswServer.use(
    http.get('*/api/v1/datos-maestros/clientes', () =>
      HttpResponse.json({ items: [], total: 0, offset: 0, limit: 200 }),
    ),
    http.get('*/api/v1/datos-maestros/clientes/ligados-aw', () =>
      HttpResponse.json([]),
    ),
  );
});

afterEach(() => {
  mswServer.resetHandlers();
  resetClientesFiltros();
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
  });
});

describe('ClientesLayout: filtros persisten entre montajes', () => {
  it('mantiene activo «Clientes sin clasificar» al montar con otro idActivo', async () => {
    const { unmount } = renderLayout(null);

    const toggle = screen.getByRole('button', {
      name: 'Clientes sin clasificar',
    });
    expect(toggle).toHaveAttribute('aria-pressed', 'false');
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-pressed', 'true');
    expect(useClientesFiltrosStore.getState().sinClasificar).toBe(true);

    unmount();

    renderLayout('c-1');

    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: 'Clientes sin clasificar' }),
      ).toHaveAttribute('aria-pressed', 'true'),
    );
  });
});
