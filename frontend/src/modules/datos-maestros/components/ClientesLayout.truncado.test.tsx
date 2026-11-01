import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
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

describe('ClientesLayout: contador y «Cargar más»', () => {
  it('muestra «200 de 205 clientes» y el botón para cargar más cuando total supera los ítems', async () => {
    responderLista(205, 200);
    renderLayout();

    const contador = await screen.findByText(/^200 de 205 clientes$/);
    expect(contador).toHaveAttribute('role', 'status');
    expect(screen.getByRole('button', { name: 'Cargar más' })).toBeInTheDocument();
  });

  it('«Cargar más» pide la siguiente página con offset y la añade a la lista', async () => {
    const offsets: (string | null)[] = [];
    mswServer.use(
      http.get('*/api/v1/datos-maestros/clientes', ({ request }) => {
        const offset = new URL(request.url).searchParams.get('offset');
        offsets.push(offset);
        const segunda = offset === '200';
        const items = segunda
          ? [clienteMinimo(201), clienteMinimo(202)]
          : Array.from({ length: 200 }, (_, i) => clienteMinimo(i + 1));
        return HttpResponse.json({ items, total: 202, offset: Number(offset ?? 0), limit: 200 });
      }),
    );
    renderLayout();

    fireEvent.click(await screen.findByRole('button', { name: 'Cargar más' }));

    expect(await screen.findByText('Cliente 202')).toBeInTheDocument();
    expect(offsets).toEqual(['0', '200']);
    expect(screen.getByText(/^202 de 202 clientes$/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cargar más' })).not.toBeInTheDocument();
  });

  it('no ofrece «Cargar más» cuando total es igual al número de ítems', async () => {
    responderLista(2, 2);
    renderLayout();

    expect(await screen.findByText('Cliente 1')).toBeInTheDocument();
    expect(screen.getByText(/^2 de 2 clientes$/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cargar más' })).not.toBeInTheDocument();
  });

  it('un solo control de estado (sin pestañas duplicadas) y el filtro de fiscales incompletos viaja al servidor', async () => {
    const urls: string[] = [];
    mswServer.use(
      http.get('*/api/v1/datos-maestros/clientes', ({ request }) => {
        urls.push(request.url);
        return HttpResponse.json({ items: [], total: 0, offset: 0, limit: 200 });
      }),
    );
    renderLayout();

    expect(screen.queryByRole('tab')).not.toBeInTheDocument();
    expect(screen.getAllByRole('combobox', { name: 'Estado' })).toHaveLength(1);
    fireEvent.click(screen.getByRole('button', { name: 'Fiscales incompletos' }));
    await waitFor(() => expect(urls.some((u) => u.includes('fiscalesIncompletos=true'))).toBe(true));
  });
});
