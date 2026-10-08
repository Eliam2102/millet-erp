import type { ComponentProps, ReactNode } from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { http, HttpResponse } from 'msw';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { accesosNavegacion, rutaPermitida } from '@/lib/nav';
import { mswServer } from '@/test/mocks/server';
import { createTestQueryClient } from '@/test/test-query-client';
import { prioridadAccesos } from '../config';
import { AccesosRapidos } from './AccesosRapidos';
import { PanelInicio } from './PanelInicio';

vi.mock('@tanstack/react-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@tanstack/react-router')>()),
  // Solo props DOM explícitas; search se refleja en href y no pasa al <a>.
  Link: ({
    children,
    to,
    search,
    className,
    'aria-label': label,
    'aria-busy': busy,
  }: {
    children?: ReactNode;
    to: string;
    search?: Record<string, string | number | boolean>;
    className?: string;
    'aria-label'?: string;
    'aria-busy'?: ComponentProps<'a'>['aria-busy'];
  }) => {
    const params = new URLSearchParams(
      Object.entries(search ?? {}).map(([k, v]) => [k, String(v)]),
    );
    return (
      <a
        href={`${to}${params.size ? `?${params}` : ''}`}
        className={className}
        aria-label={label}
        aria-busy={busy}
      >
        {children}
      </a>
    );
  },
}));
const facturas = '/api/v1/cuentas-por-pagar/facturas';
const depositos = '/api/v1/tesoreria/depositos';
const requisiciones = '/api/v1/compras/pendientes-autorizacion';
const ordenes = '/api/v1/compras/ordenes/pendientes-autorizacion';
const permisosDosFilas = [P.CuentasPorPagarFacturasLeer, P.TesoreriaDepositosConfirmar];
let client: ReturnType<typeof createTestQueryClient>;
let solicitudes: URL[];
const registrar = ({ request }: { request: Request }) => solicitudes.push(new URL(request.url));
beforeAll(() => {
  // El setup global ya inició MSW con warn; reiniciarlo permite exigir error aquí.
  mswServer.close();
  mswServer.listen({ onUnhandledRequest: 'error' });
  mswServer.events.on('request:start', registrar);
});
afterAll(() => mswServer.events.removeListener('request:start', registrar));
beforeEach(() => {
  solicitudes = [];
  client = createTestQueryClient();
  // Conserva retry: 1 del hook, acelerando solamente la espera del test.
  client.setDefaultOptions({ queries: { ...client.getDefaultOptions().queries, retryDelay: 0 } });
  useAuthStore.setState({
    ...useAuthStore.getInitialState(),
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'oid-privado-test', nombre: '  Ana María Prueba ', email: 'ana@example.test' },
    currentEmpresaId: 'empresa-activa',
    empresas: [
      { id: 'otra', razonSocial: 'Otra empresa ficticia', rfc: 'OTRA', esLaActual: false },
      { id: 'empresa-activa', razonSocial: 'Vidrios Demo SA de CV', rfc: 'DEMO', esLaActual: true },
    ],
    permisos: [],
  });
});
afterEach(() => {
  cleanup();
  client.clear();
  useAuthStore.setState(useAuthStore.getInitialState());
});
function montar(permisos: string[] = []) {
  useAuthStore.setState({ permisos });
  return render(
    <QueryClientProvider client={client}>
      <PanelInicio />
    </QueryClientProvider>,
  );
}
function responder(path: string, total: number) {
  // Patrón de Compras: wildcard del origen para las URLs absolutas de apiRequest.
  mswServer.use(
    http.get(`*${path}`, () => HttpResponse.json({ items: [], total, offset: 0, limit: 1 })),
  );
}
function pendientes() {
  return within(screen.getByRole('region', { name: 'Mis pendientes' }));
}
function fila(titulo: string) {
  return pendientes().getByRole('link', { name: new RegExp(`^${titulo}:`) });
}

describe('PanelInicio', () => {
  it('es el componente registrado en la ruta de inicio', async () => {
    // Carga la ruta real sin incorporar todo routeTree.gen al proyecto TS de tests.
    const { Route } = await vi.importActual<{ Route: { options: { component: unknown } } }>(
      '@/routes/_app/index',
    );
    expect(Route.options.component).toBe(PanelInicio);
  });
  it('solo consulta y muestra filas permitidas de CxP y depósitos', async () => {
    responder(facturas, 7);
    responder(depositos, 3);
    montar(permisosDosFilas);
    expect(await within(fila('Facturas en revisión')).findByText('7')).toBeInTheDocument();
    expect(await within(fila('Depósitos por confirmar')).findByText('3')).toBeInTheDocument();
    expect(
      pendientes()
        .getAllByRole('link')
        .map((link) => link.getAttribute('aria-label')),
    ).toEqual([
      'Facturas en revisión: 7 pendientes',
      'Depósitos por confirmar: 3 pendientes',
      'Facturas vencidas: Abrir',
    ]);
    for (const modulo of ['Compras', 'Almacén', 'CxC', 'Proveedores', 'Contabilidad'])
      expect(pendientes().queryByText(modulo)).not.toBeInTheDocument();
    expect(solicitudes.map((url) => url.pathname).sort()).toEqual([facturas, depositos].sort());
  });
  it('sin permisos no tiene filas ni HTTP y muestra exactamente los accesos disponibles', async () => {
    montar();
    await act(async () => {});
    expect(pendientes().getByText('Sin pendientes para tu rol')).toBeInTheDocument();
    expect(pendientes().queryAllByRole('listitem')).toHaveLength(0);
    const accesos = within(screen.getByRole('region', { name: 'Accesos rápidos' }));
    const esperados = accesosNavegacion([]).filter(({ to }) => to !== '/');
    expect(accesos.queryAllByRole('link').map((link) => link.getAttribute('href'))).toEqual(
      esperados.map(({ to }) => to),
    );
    if (!esperados.length)
      expect(accesos.getByText('No hay accesos disponibles para tu rol.')).toBeInTheDocument();
    expect(solicitudes).toHaveLength(0);
    expect(client.getQueryCache().getAll()).toHaveLength(0);
  });
  it('aísla el error 500 y reintenta conservando encabezado y otra fila', async () => {
    mswServer.use(
      http.get(`*${facturas}`, () =>
        HttpResponse.json({ title: 'Error', status: 500 }, { status: 500 }),
      ),
    );
    responder(depositos, 3);
    montar(permisosDosFilas);
    expect(
      await within(fila('Facturas en revisión')).findByText('No se pudo cargar'),
    ).toBeInTheDocument();
    expect(within(fila('Depósitos por confirmar')).getByText('3')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Mis pendientes' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
      /^(Buenos días|Buenas tardes|Buenas noches), Ana$/,
    );
    responder(facturas, 9);
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar Facturas en revisión' }));
    expect(await within(fila('Facturas en revisión')).findByText('9')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Reintentar/ })).not.toBeInTheDocument();
    expect(within(fila('Depósitos por confirmar')).getByText('3')).toBeInTheDocument();
  });
  it('conteo cero muestra Sin pendientes', async () => {
    responder(depositos, 0);
    montar([P.TesoreriaDepositosConfirmar]);
    expect(
      await within(fila('Depósitos por confirmar')).findByText('Sin pendientes'),
    ).toBeInTheDocument();
    expect(within(fila('Depósitos por confirmar')).queryByText('0')).not.toBeInTheDocument();
  });
  it('tiene aria-busy sin número mientras la respuesta está pendiente', async () => {
    let resolver!: () => void;
    const respuesta = new Promise<void>((resolve) => {
      resolver = resolve;
    });
    mswServer.use(
      http.get(`*${depositos}`, async () => {
        await respuesta;
        return HttpResponse.json({ items: [], total: 6 });
      }),
    );
    montar([P.TesoreriaDepositosConfirmar]);
    try {
      await waitFor(() => expect(solicitudes).toHaveLength(1));
      expect(fila('Depósitos por confirmar')).toHaveAttribute('aria-busy', 'true');
      expect(fila('Depósitos por confirmar')).toHaveAccessibleName(
        'Depósitos por confirmar: Cargando',
      );
      expect(within(fila('Depósitos por confirmar')).queryByText(/^\d+$/)).not.toBeInTheDocument();
    } finally {
      resolver();
    }
    expect(await within(fila('Depósitos por confirmar')).findByText('6')).toBeInTheDocument();
    expect(fila('Depósitos por confirmar')).not.toHaveAttribute('aria-busy');
  });
  it.each([
    { niveles: [P.ComprasRequisicionesAutorizarNivel1], nivel: '1' },
    { niveles: [P.ComprasRequisicionesAutorizarNivel2], nivel: '2' },
    {
      niveles: [P.ComprasRequisicionesAutorizarNivel1, P.ComprasRequisicionesAutorizarNivel2],
      nivel: null,
    },
  ])('filtra petición y enlace de requisiciones por nivel $nivel', async ({ niveles, nivel }) => {
    responder(requisiciones, 4);
    montar([P.ComprasRequisicionesLeer, ...niveles]);
    expect(await within(fila('Requisiciones por autorizar')).findByText('4')).toBeInTheDocument();
    expect(solicitudes).toHaveLength(1);
    expect(solicitudes[0].pathname).toBe(requisiciones);
    expect(Object.fromEntries(solicitudes[0].searchParams)).toEqual({
      offset: '0',
      limit: '1',
      ...(nivel ? { nivelPendiente: nivel } : {}),
    });
    expect(fila('Requisiciones por autorizar')).toHaveAttribute(
      'href',
      `/compras/pendientes${nivel ? `?nivelPendiente=${nivel}` : ''}`,
    );
  });
  it('OC envía page=1 y pageSize=1 y muestra totalCount', async () => {
    mswServer.use(
      http.get(`*${ordenes}`, () =>
        HttpResponse.json({ items: [], totalCount: 12, page: 1, pageSize: 1 }),
      ),
    );
    montar([P.ComprasOrdenesLeer, P.ComprasOrdenesAutorizarNivel1]);
    expect(await within(fila('OC por autorizar')).findByText('12')).toBeInTheDocument();
    expect(solicitudes).toHaveLength(1);
    expect(solicitudes[0].pathname).toBe(ordenes);
    expect(Object.fromEntries(solicitudes[0].searchParams)).toEqual({
      page: '1',
      pageSize: '1',
      nivel: 'Nivel1',
    });
  });
  it('muestra primer nombre y empresa activa sin datos de sesión', () => {
    montar();
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
      /^(Buenos días|Buenas tardes|Buenas noches), Ana$/,
    );
    expect(screen.getByText('Vidrios Demo SA de CV')).toBeInTheDocument();
    expect(screen.queryByText('Otra empresa ficticia')).not.toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(
      /Sesión activa|OID|User ID|oid-privado-test|ana@example.test|María/,
    );
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
  it('alerta cuando empresas está vacío', () => {
    useAuthStore.setState({ empresas: [], currentEmpresaId: null });
    montar();
    expect(screen.getByRole('alert')).toHaveTextContent(
      'Cuenta activa sin asignaciones de empresa o rol',
    );
  });
  it('limita accesos a ocho, ordenados por prioridad y permitidos', () => {
    const permisos = Object.values(P);
    useAuthStore.setState({ permisos });
    render(<AccesosRapidos />);
    const disponibles = accesosNavegacion(permisos).filter(({ to }) => to !== '/');
    expect(disponibles.length).toBeGreaterThan(8);
    const prioritarios = prioridadAccesos.filter((to) =>
      disponibles.some((acceso) => acceso.to === to),
    );
    expect(prioritarios.length).toBeGreaterThanOrEqual(8);
    const enlaces = screen.getAllByRole('link');
    expect(enlaces).toHaveLength(8);
    expect(enlaces.map((link) => link.getAttribute('href'))).toEqual(prioritarios.slice(0, 8));
    for (const link of enlaces)
      expect(rutaPermitida(link.getAttribute('href')!, permisos)).toBe(true);
  });
});
