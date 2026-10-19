import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ClientesLayout } from '@/modules/datos-maestros/components/ClientesLayout';
import { NuevoClienteContext } from '@/modules/datos-maestros/components/nuevo-cliente-context';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { resetClientesFiltros } from '@/modules/datos-maestros/stores/clientes-filtros-store';

/**
 * Sin cliente seleccionado, la sección de detalle solo se oculta en móvil
 * (<c>hidden md:block</c>) cuando el usuario NO tiene
 * <c>datos_maestros.clientes.gestionar</c>. Con el permiso, el panel F1-A
 * debe quedar visible también en móvil, debajo de la lista.
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

function setPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos,
    errorMessage: null,
  });
}

beforeEach(() => {
  resetClientesFiltros();
  setPermisos([PermisosCanonicos.DatosMaestrosClientesGestionar]);
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

describe('ClientesLayout: sección de detalle sin cliente seleccionado', () => {
  it('con permiso gestionar no oculta la sección de detalle en móvil', () => {
    renderLayout(null);

    const seccion = screen.getByRole('region', { name: 'Detalle de cliente' });
    expect(seccion).not.toHaveClass('hidden');
    expect(seccion).toHaveClass('block');
  });

  it('sin permiso gestionar conserva hidden md:block', () => {
    setPermisos([]);
    renderLayout(null);

    const seccion = screen.getByRole('region', { name: 'Detalle de cliente' });
    expect(seccion).toHaveClass('hidden');
    expect(seccion).toHaveClass('md:block');
  });
});
