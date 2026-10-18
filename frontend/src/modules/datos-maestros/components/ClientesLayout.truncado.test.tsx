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
import type { ClienteItem } from '@/modules/datos-maestros/api/types';

/**
 * M-25 / C-5: el aviso «Mostrando X de Y clientes» aparece solo cuando la
 * lista del servidor viene truncada (total mayor que los ítems recibidos).
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

function clienteMinimo(i: number): ClienteItem {
  return {
    id: `c-${i}`,
    clave: `CLI-${String(i).padStart(4, '0')}`,
    referenciaExterna: null,
    razonSocial: `Cliente ${i}`,
    rfc: null,
    regimenFiscal: null,
    codigoPostalFiscal: null,
    monedaDefault: 'MXN',
    esGenerico: false,
    origen: 0,
    datosFiscalesCompletos: true,
    estatus: 0,
    origenAw: null,
  };
}

function responderLista(total: number, numItems: number) {
  const items = Array.from({ length: numItems }, (_, i) => clienteMinimo(i + 1));
  mswServer.use(
    http.get('*/api/v1/datos-maestros/clientes', () =>
      HttpResponse.json({ items, total, offset: 0, limit: 200 }),
    ),
  );
}

function renderLayout() {
  return render(
    <NuevoClienteContext.Provider value={NUEVO_CLIENTE_API}>
      <ClientesLayout idActivo={null} />
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

describe('ClientesLayout: aviso de lista truncada', () => {
  it('muestra «Mostrando 200 de 205 clientes» cuando total supera los ítems recibidos', async () => {
    responderLista(205, 200);
    renderLayout();

    const aviso = await screen.findByText(/Mostrando 200 de 205 clientes/);
    expect(aviso).toHaveAttribute('role', 'status');
  });

  it('no muestra el aviso cuando total es igual al número de ítems', async () => {
    responderLista(2, 2);
    renderLayout();

    expect(await screen.findByText('Cliente 1')).toBeInTheDocument();
    expect(screen.queryByText(/Mostrando/)).not.toBeInTheDocument();
  });
});
