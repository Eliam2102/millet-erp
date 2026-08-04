import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { CanalCobranza, ResultadoCobranza } from '@/features/cxc/api/types';
import { useAuthStore } from '@/lib/auth/auth-store';

const searchMock = vi.hoisted(() => ({ current: {} as Record<string, unknown> }));

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => searchMock.current,
}));

const abrirSpy = vi.fn();
vi.mock('@/features/cxc/components/registrar-gestion-context', () => ({
  useRegistrarGestion: () => ({
    abrir: abrirSpy,
    cerrar: () => {},
    setDirty: () => {},
  }),
}));

// Import DESPUÉS de los mocks para que la página los consuma.
import { CobranzaPage } from '@/features/cxc/pages/CobranzaPage';

const COBRANZA = '*/api/v1/cuentas-por-cobrar/cobranza';
const LOOKUP = '*/api/v1/cuentas-por-cobrar/clientes-lookup';
const USUARIOS = '*/api/v1/identidad/usuarios';

beforeEach(() => {
  abrirSpy.mockClear();
  searchMock.current = {};
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: [
      'cuentas_por_cobrar.cartera.leer',
      'cuentas_por_cobrar.cobranza.registrar',
      'cuentas_por_cobrar.lineas-credito.leer',
    ],
    errorMessage: null,
  });
  mswServer.use(
    http.get(LOOKUP, () => HttpResponse.json([])),
    http.get(USUARIOS, () =>
      HttpResponse.json({
        items: [
          {
            id: 'u-1',
            email: 'ana@millet.mx',
            nombre: 'Ana Cobranza',
            departamentoId: null,
            activo: true,
          },
        ],
        offset: 0,
        limit: 200,
        total: 1,
      }),
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

describe('<CobranzaPage> — smoke', () => {
  it('sin cliente: CTA de elegir cliente (filtro obligatorio)', async () => {
    render(<CobranzaPage />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText(/Elige un cliente para ver su bitácora/i),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Registrar gestión/i }),
    ).toBeInTheDocument();
  });

  it('con cliente: renderiza el timeline con promesa destacada', async () => {
    searchMock.current = { clienteId: 'cli-1' };
    mswServer.use(
      http.get(COBRANZA, () =>
        HttpResponse.json({
          items: [
            {
              id: 'sc-1',
              clienteId: 'cli-1',
              fecha: '2026-07-14T10:00:00Z',
              usuarioId: 'u-1',
              canal: CanalCobranza.Llamada,
              resultado: ResultadoCobranza.PromesaPago,
              montoComprometido: 25000,
              fechaComprometida: '2026-07-21',
              nota: 'Cliente promete pagar la próxima semana.',
            },
            {
              id: 'sc-2',
              clienteId: 'cli-1',
              fecha: '2026-07-10T10:00:00Z',
              usuarioId: 'u-1',
              canal: CanalCobranza.Correo,
              resultado: ResultadoCobranza.SinRespuesta,
              montoComprometido: null,
              fechaComprometida: null,
              nota: 'Se envió estado de cuenta.',
            },
          ],
          offset: 0,
          limit: 100,
          total: 2,
        }),
      ),
    );
    render(<CobranzaPage />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText(/Cliente promete pagar/i),
    ).toBeInTheDocument();
    expect(screen.getByText('Promesa de pago')).toBeInTheDocument();
    expect(screen.getByText(/Compromiso:/i)).toBeInTheDocument();
    expect(await screen.findAllByText(/Ana Cobranza/)).toHaveLength(2);
  });

  it('con cliente y sin gestiones: empty con CTA', async () => {
    searchMock.current = { clienteId: 'cli-1' };
    mswServer.use(
      http.get(COBRANZA, () =>
        HttpResponse.json({ items: [], offset: 0, limit: 100, total: 0 }),
      ),
    );
    render(<CobranzaPage />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText(/Sin gestiones para este cliente/i),
    ).toBeInTheDocument();
  });

  it('estado error con retry', async () => {
    searchMock.current = { clienteId: 'cli-1' };
    mswServer.use(
      http.get(COBRANZA, () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Boom', status: 500 },
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<CobranzaPage />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText(/No se pudo cargar la bitácora/i),
    ).toBeInTheDocument();
  });

  it('sin permiso registrar: botón oculto', async () => {
    useAuthStore.setState({
      permisos: [
        'cuentas_por_cobrar.cartera.leer',
        'cuentas_por_cobrar.lineas-credito.leer',
      ],
    });
    render(<CobranzaPage />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText(/Elige un cliente para ver su bitácora/i),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Registrar gestión/i }),
    ).not.toBeInTheDocument();
  });
});
