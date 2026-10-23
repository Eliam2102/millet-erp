import type { ReactNode } from 'react';
import { StrictMode } from 'react';
import { QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { http, HttpResponse } from 'msw';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { mswServer } from '@/test/mocks/server';
import { createTestQueryClient } from '@/test/test-query-client';
import { piezasInicio, piezasPermitidas } from '../config';
import { componerPiezas, type ResumenPieza } from '../api/usePiezasInicio';
import { PanelInicio } from './PanelInicio';

vi.mock('@tanstack/react-router', async (original) => ({
  ...(await original<typeof import('@tanstack/react-router')>()),
  Link: ({ to, children, className, 'aria-busy': busy }: { to: string; children?: ReactNode; className?: string; 'aria-busy'?: boolean }) =>
    <a href={to} className={className} aria-busy={busy}>{children}</a>,
}));
vi.mock('@/features/compras/components/nueva-requisicion-context', () => ({ useNuevaRequisicion: () => ({ abrir: vi.fn() }) }));
vi.mock('@/features/compras/ordenes/components/nueva-orden-compra-context', () => ({ useNuevaOrdenCompra: () => ({ abrir: vi.fn() }) }));
vi.mock('@/features/cxc/components/registrar-gestion-context', () => ({ useRegistrarGestion: () => ({ abrir: vi.fn() }) }));

const perfiles = {
  Compras: [P.ComprasRequisicionesLeer, P.ComprasRequisicionesCrear, P.ComprasOrdenesLeer],
  Almacén: [P.AlmacenEntradasLeer, P.AlmacenSalidasLeerTodas, P.AlmacenInventariosLeer],
  Cobranza: [P.CuentasPorCobrarCarteraLeer, P.TesoreriaDepositosConfirmar],
  Tesorería: [P.TesoreriaPasivosVer, P.TesoreriaCuentasVer, P.TesoreriaDepositosConfirmar, P.TesoreriaMovimientosVer],
  Administrador: Object.values(P),
};
let llamadas: URL[];
let client: ReturnType<typeof createTestQueryClient>;
const resumen: ResumenPieza = { valor: 3, total: 3, contexto: '3 documentos de prueba en tu alcance', urgentes: 0, etapas: [{ etiqueta: 'Por revisar', total: 3 }] };
beforeEach(() => {
  llamadas = [];
  client = createTestQueryClient();
  useAuthStore.setState({ ...useAuthStore.getInitialState(), status: 'authenticated', accessToken: 'test',
    user: { id: 'usuario-prueba', nombre: 'Ana Prueba', email: 'ana@example.test' },
    currentEmpresaId: 'empresa-prueba', empresas: [{ id: 'empresa-prueba', razonSocial: 'Empresa ficticia', rfc: 'DEMO', esLaActual: true }],
  });
  mswServer.use(http.get('*/api/*', ({ request }) => {
    const url = new URL(request.url);
    llamadas.push(url);
    if (url.pathname.includes('/inicio/')) return HttpResponse.json(resumen);
    if (url.pathname.endsWith('/kpis')) return HttpResponse.json({ countPartidasAbiertas: 1, countAtrasadas: 1 });
    if (url.pathname.endsWith('/pendientes-autorizacion') && url.pathname.includes('/ordenes/')) return HttpResponse.json({ totalCount: 2, items: [] });
    return HttpResponse.json({ items: [], total: 2, offset: 0, limit: 1 });
  }));
});
afterEach(() => { cleanup(); client.clear(); useAuthStore.setState(useAuthStore.getInitialState()); });
function montar(permisos: string[], estricto = false) {
  useAuthStore.setState({ permisos });
  const contenido = <QueryClientProvider client={client}><PanelInicio /></QueryClientProvider>;
  return render(estricto ? <StrictMode>{contenido}</StrictMode> : contenido);
}
async function listo() { await waitFor(() => expect(client.isFetching()).toBe(0)); }
const indicadores = () => within(screen.getByRole('region', { name: 'Indicadores' }));

describe('INI-ROL: pantalla y peticiones por permiso', () => {
  it.each(Object.entries(perfiles))('%s consulta solo fuentes autorizadas y cada endpoint una vez', async (_, permisos) => {
    const vista = montar(permisos, true);
    await listo();
    const permitidas = piezasPermitidas(permisos);
    const visuales = permitidas.filter(p => p.tipo === 'visual').sort((a, b) => b.prioridad - a.prioridad).slice(0, 2);
    const esperadas = new Set([...permitidas.filter(p => p.tipo === 'indicador'), ...visuales].flatMap(p => p.fuentes.map(f => f.endpoint)));
    const resueltas = llamadas.filter(u => u.pathname.includes('/inicio/')).map(u => u.pathname);
    expect(new Set(resueltas)).toEqual(esperadas);
    expect(resueltas).toHaveLength(esperadas.size);
    expect(new Set(llamadas.map(u => u.href)).size).toBe(llamadas.length);
    expect(vista.container.querySelectorAll('[data-pieza]').length).toBeLessThanOrEqual(5);
    const antes = llamadas.length;
    vista.rerender(<StrictMode><QueryClientProvider client={client}><PanelInicio /></QueryClientProvider></StrictMode>);
    await act(async () => {});
    expect(llamadas).toHaveLength(antes);
  });
  it('capturista de Compras no ve ni consulta bancos, cartera o pasivos', async () => {
    montar(perfiles.Compras);
    await listo();
    expect(screen.queryByText('Saldo en bancos')).not.toBeInTheDocument();
    expect(screen.queryByText('Por cobrar vencido')).not.toBeInTheDocument();
    expect(screen.queryByText('Por pagar esta semana')).not.toBeInTheDocument();
    expect(screen.getByText('Tus requisiciones')).toBeInTheDocument();
    expect(screen.getByText('Circuito de compra')).toBeInTheDocument();
    expect(llamadas.some(u => /tesoreria|cuentas-por-cobrar/.test(u.pathname))).toBe(false);
    expect(screen.queryByText('Lo último que hiciste')).not.toBeInTheDocument();
    expect(llamadas.some(u => u.pathname.includes('/admin/auditoria'))).toBe(false);
  });
  it('Almacén muestra recepciones y vales; no muestra cartera ni bancos', async () => {
    montar(perfiles.Almacén);
    await listo();
    expect(indicadores().getByText('Recepciones pendientes')).toBeInTheDocument();
    expect(indicadores().getByText('Vales por regularizar')).toBeInTheDocument();
    expect(indicadores().getByText('Conteos abiertos')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Accesos de tu módulo' })).toBeInTheDocument();
    expect(llamadas.some(u => u.pathname.includes('tesoreria') || u.pathname.includes('cuentas-por-cobrar'))).toBe(false);
  });
  it('Cobranza ve su circuito de cartera sin consultar Facturación sin permiso', async () => {
    montar(perfiles.Cobranza);
    await listo();
    expect(indicadores().getByText('Por cobrar vencido')).toBeInTheDocument();
    expect(screen.getByText('Circuito de venta')).toBeInTheDocument();
    expect(llamadas.some(u => u.pathname.includes('facturacion'))).toBe(false);
  });
  it('Tesorería ve bancos, pasivos, depósitos y flujo', async () => {
    montar(perfiles.Tesorería);
    await listo();
    for (const titulo of ['Saldo en bancos', 'Por pagar esta semana', 'Depósitos por confirmar'])
      expect(indicadores().getByText(titulo)).toBeInTheDocument();
    expect(screen.getByText('Flujo de la semana')).toBeInTheDocument();
    expect(screen.queryByText('Por cobrar vencido')).not.toBeInTheDocument();
  });
  it('tres piezas permitidas producen exactamente tres tarjetas, sin espacios vacíos', async () => {
    const vista = montar([P.TesoreriaCuentasVer, P.TesoreriaPasivosVer, P.ContabilidadPeriodoLeer]);
    await listo();
    expect(vista.container.querySelectorAll('[data-pieza]')).toHaveLength(3);
    expect(screen.getByText('Accesos de tu módulo')).toBeInTheDocument();
    expect(indicadores().queryByText(/bloquead/i)).not.toBeInTheDocument();
  });
  it('cero con contexto sigue siendo útil y no crea tarjetas bloqueadas', async () => {
    mswServer.use(http.get('*/api/v1/tesoreria/inicio/bancos', () => HttpResponse.json({ ...resumen, valor: 0, total: 0, contexto: 'Sin cuentas activas en MXN en tus sucursales' })));
    montar([P.TesoreriaCuentasVer]); await listo();
    expect(indicadores().getByText('$0')).toBeInTheDocument();
    expect(indicadores().getByText('Sin cuentas activas en MXN en tus sucursales')).toBeInTheDocument();
  });
  it('sin permisos no consulta fuentes ni presenta indicadores', async () => {
    montar([]); await listo();
    expect(llamadas).toHaveLength(0);
    expect(screen.queryByRole('region', { name: 'Indicadores' })).not.toBeInTheDocument();
    expect(screen.getByText('No tienes pendientes')).toBeInTheDocument();
  });
  it('envía la sucursal del topbar y vuelve a consultar una vez al cambiarla', async () => {
    useAuthStore.setState({ currentSucursalId: 'sucursal-uno' });
    montar([P.TesoreriaCuentasVer]); await listo();
    expect(llamadas).toHaveLength(1);
    expect(llamadas[0].searchParams.get('sucursalId')).toBe('sucursal-uno');
    act(() => useAuthStore.setState({ currentSucursalId: 'sucursal-dos' }));
    await listo();
    expect(llamadas).toHaveLength(2);
    expect(llamadas[1].searchParams.get('sucursalId')).toBe('sucursal-dos');
  });
  it('al revocar permisos retira datos ya cacheados sin otra petición', async () => {
    montar([P.TesoreriaCuentasVer]); await listo();
    act(() => useAuthStore.setState({ permisos: [] }));
    expect(screen.queryByText('Saldo en bancos')).not.toBeInTheDocument();
    expect(llamadas).toHaveLength(1);
  });
  it('un error de resumen no repite llamadas ni inventa un cero', async () => {
    mswServer.use(http.get('*/api/v1/tesoreria/inicio/bancos', ({ request }) => {
      llamadas.push(new URL(request.url)); return HttpResponse.json({}, { status: 500 });
    }));
    montar([P.TesoreriaCuentasVer]); await listo();
    expect(llamadas).toHaveLength(1);
    expect(indicadores().getByText('No se pudieron cargar los datos.')).toBeInTheDocument();
    expect(indicadores().queryByText('$0')).not.toBeInTheDocument();
  });
  it('las pestañas filtran la bandeja existente', async () => {
    montar([P.ComprasRequisicionesLeer, P.ComprasRequisicionesAutorizarNivel1, P.CuentasPorPagarFacturasLeer]);
    await listo();
    const region = within(screen.getByRole('region', { name: 'Requiere tu acción' }));
    fireEvent.click(region.getByRole('tab', { name: /Autorizar/ }));
    expect(region.getByText('Requisiciones por autorizar')).toBeInTheDocument();
    expect(region.queryByText('Facturas en revisión')).not.toBeInTheDocument();
    fireEvent.click(region.getByRole('tab', { name: /Revisar/ }));
    expect(region.getByText('Facturas en revisión')).toBeInTheDocument();
  });
  it('no entra en refetch al montar y rerenderizar en StrictMode', async () => {
    montar([P.TesoreriaCuentasVer], true); await listo();
    expect(llamadas).toHaveLength(1);
  });
  it('Hoy/Semana/Mes filtra solo los circuitos y no vuelve a consultar saldos', async () => {
    montar([...perfiles.Compras, P.TesoreriaCuentasVer]); await listo();
    const circuito = '/api/v1/compras/inicio/circuito';
    expect(llamadas.find(u => u.pathname === circuito)?.searchParams.get('periodo')).toBe('Mes');
    fireEvent.click(screen.getByRole('button', { name: 'Hoy' })); await listo();
    expect(llamadas.filter(u => u.pathname === circuito).map(u => u.searchParams.get('periodo'))).toEqual(['Mes', 'Hoy']);
    expect(llamadas.filter(u => u.pathname.endsWith('/bancos'))).toHaveLength(1);
    fireEvent.click(screen.getByRole('button', { name: 'Semana' })); await listo();
    expect(llamadas.filter(u => u.pathname === circuito)).toHaveLength(3);
    fireEvent.click(screen.getByRole('button', { name: 'Mes' })); await listo();
    expect(llamadas.filter(u => u.pathname === circuito)).toHaveLength(3);
  });
  it('conserva la ruta real de Inicio', async () => {
    const { Route } = await vi.importActual<{ Route: { options: { component: unknown } } }>('@/routes/_app/index');
    expect(Route.options.component).toBe(PanelInicio);
  });
});

describe('composición pura', () => {
  it('elige máximo cinco por urgencia y prioridad', () => {
    const piezas = piezasInicio.filter(p => p.tipo === 'indicador');
    const items = piezas.map(pieza => ({ pieza, data: { ...resumen, urgentes: pieza.id === 'bancos' ? 5 : 0 }, estado: 'listo' as const }));
    const compuesta = componerPiezas(items);
    expect(compuesta.indicadores).toHaveLength(5);
    expect(compuesta.indicadores[0].pieza.id).toBe('bancos');
  });
});
