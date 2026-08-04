import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaLineasCredito } from '@/features/cxc/pages/BandejaLineasCredito';
import {
  EstadoLineaCredito,
  OrigenLineaCredito,
} from '@/features/cxc/api/types';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

// El botón "Nueva línea" consume el provider shell-level; en el test lo
// reemplazamos por un noop para no montar todo el shell.
const abrirSpy = vi.fn();
vi.mock('@/features/cxc/components/nueva-linea-credito-context', () => ({
  useNuevaLineaCredito: () => ({
    abrir: abrirSpy,
    cerrar: () => {},
    setDirty: () => {},
  }),
}));

const LINEAS = '*/api/v1/cuentas-por-cobrar/lineas-credito';
const LOOKUP = '*/api/v1/cuentas-por-cobrar/clientes-lookup';

beforeEach(() => {
  abrirSpy.mockClear();
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: [
      'cuentas_por_cobrar.lineas-credito.leer',
      'cuentas_por_cobrar.lineas-credito.gestionar',
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

describe('<BandejaLineasCredito> — smoke', () => {
  it('estado empty + botón Nueva línea visible con permiso gestionar', async () => {
    mswServer.use(
      http.get(LINEAS, () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<BandejaLineasCredito />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin líneas de crédito/i)).toBeInTheDocument(),
    );
    expect(
      screen.getByRole('button', { name: /Nueva línea/i }),
    ).toBeInTheDocument();
  });

  it('renderiza una línea con razón social resuelta del lookup', async () => {
    mswServer.use(
      http.get(LINEAS, () =>
        HttpResponse.json({
          items: [
            {
              id: 'lc-1',
              clienteId: 'cli-1',
              moneda: 'MXN',
              limite: 500000,
              origen: OrigenLineaCredito.Solunion,
              plazoDias: 30,
              clasificacion: 'A',
              estado: EstadoLineaCredito.Activa,
              motivoBloqueo: null,
              version: 1,
            },
          ],
          offset: 0,
          limit: 200,
          total: 1,
        }),
      ),
      http.get(LOOKUP, () =>
        HttpResponse.json([
          {
            id: 'cli-1',
            clave: 'C001',
            rfc: 'AAA010101AAA',
            razonSocial: 'ACME Vidrios SA',
          },
        ]),
      ),
    );
    render(<BandejaLineasCredito />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('ACME Vidrios SA')).toBeInTheDocument(),
    );
    expect(screen.getByText('Activa')).toBeInTheDocument();
    expect(screen.getByText('SOLUNION')).toBeInTheDocument();
  });

  it('estado error con retry', async () => {
    mswServer.use(
      http.get(LINEAS, () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Boom', status: 500 },
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<BandejaLineasCredito />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/No se pudieron cargar las líneas/i),
      ).toBeInTheDocument(),
    );
  });

  it('sin permiso gestionar no muestra Nueva línea', async () => {
    useAuthStore.setState({
      permisos: ['cuentas_por_cobrar.lineas-credito.leer'],
    });
    mswServer.use(
      http.get(LINEAS, () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<BandejaLineasCredito />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin líneas de crédito/i)).toBeInTheDocument(),
    );
    expect(
      screen.queryByRole('button', { name: /Nueva línea/i }),
    ).not.toBeInTheDocument();
  });
});
