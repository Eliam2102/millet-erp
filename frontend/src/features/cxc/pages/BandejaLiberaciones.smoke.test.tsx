import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaLiberaciones } from '@/features/cxc/pages/BandejaLiberaciones';
import {
  ReglaAplicadaLiberacion,
  ResultadoLiberacion,
} from '@/features/cxc/api/types';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

const LIB = '*/api/v1/cuentas-por-cobrar/liberaciones';
const AUT = '*/api/v1/cuentas-por-cobrar/autorizaciones';
const LOOKUP = '*/api/v1/cuentas-por-cobrar/clientes-lookup';
const USUARIOS = '*/api/v1/identidad/usuarios';

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: [
      'cuentas_por_cobrar.liberacion.decidir',
      'cuentas_por_cobrar.liberacion.override',
      'cuentas_por_cobrar.lineas-credito.leer',
    ],
    errorMessage: null,
  });
  // Defaults compartidos: bandejas vacías salvo override por test.
  mswServer.use(
    http.get(AUT, () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
    http.get(LOOKUP, () => HttpResponse.json([])),
    http.get(USUARIOS, () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
  );
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

describe('<BandejaLiberaciones> — smoke', () => {
  it('estado empty + acciones visibles con permisos', async () => {
    mswServer.use(
      http.get(LIB, () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<BandejaLiberaciones />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/Sin decisiones de liberación/i),
      ).toBeInTheDocument(),
    );
    expect(
      screen.getByRole('button', { name: /Decidir liberación/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Nueva autorización/i }),
    ).toBeInTheDocument();
  });

  it('renderiza una decisión con resultado, regla y cliente resuelto', async () => {
    mswServer.use(
      http.get(LIB, () =>
        HttpResponse.json({
          items: [
            {
              id: 'dl-1',
              pedidoRef: '3000123456',
              clienteId: 'cli-1',
              moneda: 'MXN',
              montoPedido: 15000,
              creditoDisponibleSnapshot: 38000,
              resultado: ResultadoLiberacion.LiberadoConOverride,
              reglaAplicada: ReglaAplicadaLiberacion.Override,
              overrideId: 'ac-1',
              decididoPor: 'u-test',
              decididoEn: '2026-07-14T10:00:00Z',
            },
          ],
          offset: 0,
          limit: 200,
          total: 1,
        }),
      ),
      http.get(LOOKUP, () =>
        HttpResponse.json([
          { id: 'cli-1', clave: 'C001', rfc: 'AAA010101AAA', razonSocial: 'ACME SA' },
        ]),
      ),
    );
    render(<BandejaLiberaciones />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('3000123456')).toBeInTheDocument(),
    );
    // El lookup de nombres resuelve DESPUÉS de la lista (query encadenada
    // por ids) — findByText espera ese segundo ciclo.
    expect(await screen.findByText('ACME SA')).toBeInTheDocument();
    expect(screen.getByText('Liberado con override')).toBeInTheDocument();
    expect(screen.getByText('Override autorizado')).toBeInTheDocument();
  });

  it('sin permiso override: no muestra "Nueva autorización"', async () => {
    useAuthStore.setState({
      permisos: ['cuentas_por_cobrar.liberacion.decidir'],
    });
    mswServer.use(
      http.get(LIB, () =>
        HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
      ),
    );
    render(<BandejaLiberaciones />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/Sin decisiones de liberación/i),
      ).toBeInTheDocument(),
    );
    expect(
      screen.queryByRole('button', { name: /Nueva autorización/i }),
    ).not.toBeInTheDocument();
  });

  it('estado error con retry', async () => {
    mswServer.use(
      http.get(LIB, () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Boom', status: 500 },
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<BandejaLiberaciones />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/No se pudieron cargar las decisiones/i),
      ).toBeInTheDocument(),
    );
  });
});
