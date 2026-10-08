import type { ComponentProps, ReactNode } from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { http, HttpResponse } from 'msw';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { rutaPermitida } from '@/lib/nav';
import { mswServer } from '@/test/mocks/server';
import { createTestQueryClient } from '@/test/test-query-client';
import { modulosInicio } from '../modulos';
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
  return within(screen.getByRole('region', { name: 'Requiere tu acción' }));
}
function indicadores() {
  return within(screen.getByRole('region', { name: 'Indicadores' }));
}
async function fila(titulo: string) {
  return pendientes().findByRole('link', { name: new RegExp(`^${titulo}:`) });
}

describe('PanelInicio v2', () => {
  it('es el componente registrado en la ruta de inicio', async () => {
    const { Route } = await vi.importActual<{ Route: { options: { component: unknown } } }>(
      '@/routes/_app/index',
    );
    expect(Route.options.component).toBe(PanelInicio);
  });
  it('solo consulta y muestra KPI y filas permitidos', async () => {
    responder(facturas, 7);
    responder(depositos, 3);
    montar(permisosDosFilas);
    expect(await within(await fila('Facturas en revisión')).findByText('7')).toBeInTheDocument();
    expect(await within(await fila('Depósitos por confirmar')).findByText('3')).toBeInTheDocument();
    expect(pendientes().getAllByRole('link')).toHaveLength(2);
    expect(indicadores().getAllByRole('link')).toHaveLength(2);
    expect(indicadores().getByText('7')).toBeInTheDocument();
    expect(indicadores().getByText('3')).toBeInTheDocument();
    for (const modulo of ['Compras', 'Almacén', 'CxC', 'Proveedores', 'Contabilidad'])
      expect(pendientes().queryByText(modulo)).not.toBeInTheDocument();
    expect(solicitudes.map((url) => url.pathname).sort()).toEqual([facturas, depositos].sort());
    expect(screen.queryByRole('region', { name: 'Recientes' })).not.toBeInTheDocument();
  });
  it('sin permisos no tiene filas ni HTTP y usa módulos permitidos', async () => {
    montar();
    await act(async () => {});
    expect(pendientes().getByText('No tienes pendientes')).toBeInTheDocument();
    expect(pendientes().queryAllByRole('listitem')).toHaveLength(0);
    expect(indicadores().queryAllByRole('link')).toHaveLength(0);
    const modulos = within(screen.getByRole('region', { name: 'Módulos' }));
    expect(modulos.queryAllByRole('link').map((l) => l.getAttribute('href'))).toEqual(
      modulosInicio([]).map((a) => a.to),
    );
    expect(solicitudes).toHaveLength(0);
    expect(client.getQueryCache().getAll()).toHaveLength(0);
  });
  it('aísla error 500 y permite reintentar sin romper el resto', async () => {
    mswServer.use(
      http.get(`*${facturas}`, () =>
        HttpResponse.json({ title: 'Error', status: 500 }, { status: 500 }),
      ),
    );
    responder(depositos, 3);
    montar(permisosDosFilas);
    const boton = await screen.findByRole('button', { name: 'Reintentar Facturas en revisión' });
    expect(within(await fila('Depósitos por confirmar')).getByText('3')).toBeInTheDocument();
    expect(indicadores().getByText('No se pudo cargar')).toBeInTheDocument();
    expect(pendientes().queryByText('No tienes pendientes')).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
      /^(Buenos días|Buenas tardes|Buenas noches), Ana$/,
    );
    responder(facturas, 9);
    fireEvent.click(boton);
    expect(within(await fila('Facturas en revisión')).getByText('9')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Reintentar/ })).not.toBeInTheDocument();
  });
  it('conteo cero deja la bandeja vacía y un KPI normal sin tag', async () => {
    responder(depositos, 0);
    montar([P.TesoreriaDepositosConfirmar]);
    expect(await pendientes().findByText('No tienes pendientes')).toBeInTheDocument();
    expect(pendientes().queryAllByRole('link')).toHaveLength(0);
    expect(indicadores().getByText('0')).toBeInTheDocument();
    expect(indicadores().queryByText('Atención')).not.toBeInTheDocument();
    expect(indicadores().queryByText('Crítico')).not.toBeInTheDocument();
  });
  it('muestra carga sin inventar un número o un estado vacío', async () => {
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
      expect(indicadores().getByRole('link')).toHaveAttribute('aria-busy', 'true');
      expect(indicadores().queryByText(/^\d+$/)).not.toBeInTheDocument();
      expect(pendientes().queryByText('No tienes pendientes')).not.toBeInTheDocument();
    } finally {
      resolver();
    }
    expect(within(await fila('Depósitos por confirmar')).getByText('6')).toBeInTheDocument();
  });
  it.each([
    { niveles: [P.ComprasRequisicionesAutorizarNivel1], nivel: '1' },
    { niveles: [P.ComprasRequisicionesAutorizarNivel2], nivel: '2' },
    {
      niveles: [P.ComprasRequisicionesAutorizarNivel1, P.ComprasRequisicionesAutorizarNivel2],
      nivel: null,
    },
  ])('filtra petición y enlaces de requisiciones por nivel $nivel', async ({ niveles, nivel }) => {
    responder(requisiciones, 4);
    montar([P.ComprasRequisicionesLeer, ...niveles]);
    const link = await fila('Requisiciones por autorizar');
    expect(solicitudes).toHaveLength(1);
    expect(Object.fromEntries(solicitudes[0].searchParams)).toEqual({
      offset: '0',
      limit: '1',
      ...(nivel ? { nivelPendiente: nivel } : {}),
    });
    const href = `/compras/pendientes${nivel ? `?nivelPendiente=${nivel}` : ''}`;
    expect(link).toHaveAttribute('href', href);
    expect(indicadores().getByRole('link')).toHaveAttribute('href', href);
  });
  it('OC usa totalCount, FIFO y antigüedad urgente con danger y ring crítico', async () => {
    const fecha = new Date(Date.now() - 9 * 86400000).toISOString();
    mswServer.use(
      http.get(`*${ordenes}`, () =>
        HttpResponse.json({
          items: [{ fechaDocumento: fecha }],
          totalCount: 12,
          page: 1,
          pageSize: 1,
        }),
      ),
    );
    montar([P.ComprasOrdenesLeer, P.ComprasOrdenesAutorizarNivel1]);
    const link = await fila('OC por autorizar');
    expect(within(link).getByText('12')).toBeInTheDocument();
    expect(within(link).getByText('hace 9 días')).toHaveClass('text-danger-fg', 'font-medium');
    expect(indicadores().getByText('Crítico')).toBeInTheDocument();
    expect(indicadores().getByRole('link').parentElement).toHaveClass('ring-danger-ring');
    expect(Object.fromEntries(solicitudes[0].searchParams)).toEqual({
      page: '1',
      pageSize: '1',
      nivel: 'Nivel1',
    });
  });
  it('no usa fechas de una API que entrega el más reciente primero', async () => {
    mswServer.use(
      http.get(`*${depositos}`, () =>
        HttpResponse.json({ items: [{ createdAt: '2020-01-01' }], total: 1 }),
      ),
    );
    montar([P.TesoreriaDepositosConfirmar]);
    const link = await fila('Depósitos por confirmar');
    expect(link).not.toHaveTextContent(/hace|ayer|hoy/);
  });
  it('elige cuatro KPI por prioridad entre cinco elegibles, conservando todas las filas positivas', async () => {
    responder(facturas, 1);
    responder(depositos, 11);
    responder(requisiciones, 2);
    mswServer.use(http.get(`*${ordenes}`, () => HttpResponse.json({ items: [], totalCount: 26 })));
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/partidas-abiertas/kpis', () =>
        HttpResponse.json({ countPartidasAbiertas: 7, countAtrasadas: 1 }),
      ),
    );
    montar([
      ...permisosDosFilas,
      P.ComprasRequisicionesLeer,
      P.ComprasRequisicionesAutorizarNivel1,
      P.ComprasOrdenesLeer,
      P.ComprasOrdenesAutorizarNivel1,
      P.ComprasOrdenesReportesPartidasAbiertas,
    ]);
    await waitFor(() => expect(pendientes().getAllByRole('link')).toHaveLength(5));
    const links = indicadores().getAllByRole('link');
    expect(links).toHaveLength(4);
    expect(links.map((l) => l.getAttribute('aria-label')?.split(':')[0])).toEqual([
      'OC por autorizar',
      'Depósitos por confirmar',
      'Partidas abiertas',
      'Requisiciones por autorizar',
    ]);
    expect(indicadores().queryByText('Facturas en revisión')).not.toBeInTheDocument();
  });
  it('muestra el nombre y empresa; no inventa rol ni expone datos de sesión', () => {
    montar();
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
      /^(Buenos días|Buenas tardes|Buenas noches), Ana$/,
    );
    expect(screen.getByText('Vidrios Demo SA de CV')).toBeInTheDocument();
    expect(screen.getByText('[ROL]')).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(/OID|oid-privado-test|ana@example.test|María/);
  });
  it('conserva la alerta cuando no hay empresas', () => {
    useAuthStore.setState({ empresas: [], currentEmpresaId: null });
    montar();
    expect(screen.getByRole('alert')).toHaveTextContent(
      'Cuenta activa sin asignaciones de empresa o rol',
    );
  });
  it('presenta una tarjeta por módulo, con rutas permitidas', () => {
    const permisos = Object.values(P);
    useAuthStore.setState({ permisos });
    render(<AccesosRapidos />);
    const esperados = modulosInicio(permisos);
    const enlaces = screen.getAllByRole('link');
    expect(enlaces.map((l) => l.getAttribute('href'))).toEqual(esperados.map((a) => a.to));
    expect(new Set(esperados.map((a) => a.modulo)).size).toBe(enlaces.length);
    for (const l of enlaces) expect(rutaPermitida(l.getAttribute('href')!, permisos)).toBe(true);
  });
  it('consulta Recientes por usuario y empresa solo con permiso de auditoría', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/auditoria', () =>
        HttpResponse.json({
          items: [
            {
              id: 'evento',
              entidadEtiqueta: 'OC-PRUEBA',
              resumen: 'Autorizó la orden',
              timestamp: new Date().toISOString(),
            },
          ],
          total: 1,
        }),
      ),
    );
    montar([P.AdminAuditoriaLeer]);
    const recientes = within(screen.getByRole('region', { name: 'Recientes' }));
    expect(await recientes.findByText('OC-PRUEBA')).toHaveClass('font-mono', 'text-brand');
    expect(recientes.getByText('Autorizó la orden')).toBeInTheDocument();
    expect(solicitudes).toHaveLength(1);
    expect(solicitudes[0].searchParams.get('usuarioId')).toBe('oid-privado-test');
    expect(solicitudes[0].searchParams.get('empresaId')).toBe('empresa-activa');
    expect(solicitudes[0].searchParams.get('limit')).toBe('4');
  });
});
