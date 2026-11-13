import { afterEach, beforeEach, expect, it } from 'vitest';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createBrowserHistory, createRootRoute, createRoute, createRouter, Outlet, RouterProvider } from '@tanstack/react-router';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { BandejaRequisiciones } from './BandejaRequisiciones';
import { BandejaOrdenesCompra } from '../ordenes/pages/BandejaOrdenesCompra';
import { NuevaOrdenCompraContext } from '../ordenes/components/nueva-orden-compra-context';
import { BandejaSearchSchema } from '../lib/bandeja-search-schema';
import { BandejaOcSearchSchema } from '../ordenes/lib/bandeja-oc-search-schema';

const peticiones: { tipo: string; cantidad: number; posicion: number }[] = [];
const noop = { abrir: () => {}, cerrar: () => {}, setDirty: () => {} };
const root = createRootRoute({ component: Outlet });
const app = createRoute({ getParentRoute: () => root, id: '_app', component: Outlet });
const compras = createRoute({ getParentRoute: () => app, path: 'compras', component: Outlet });
const rq = createRoute({ getParentRoute: () => compras, path: 'requisiciones', component: Outlet });
const oc = createRoute({ getParentRoute: () => compras, path: 'ordenes', component: Outlet });
const rqLista = createRoute({ getParentRoute: () => rq, path: '/', validateSearch: (s) => BandejaSearchSchema.parse(s), component: BandejaRequisiciones });
const ocLista = createRoute({ getParentRoute: () => oc, path: '/', validateSearch: (s) => BandejaOcSearchSchema.parse(s), component: BandejaOrdenesCompra });
const tree = root.addChildren([app.addChildren([compras.addChildren([rq.addChildren([rqLista]), oc.addChildren([ocLista])])])]);
let cerrar: (() => void) | undefined;
beforeEach(() => {
  localStorage.clear(); peticiones.length = 0;
  window.history.replaceState({}, '', '/compras/requisiciones');
  useAuthStore.setState({ status: 'authenticated', accessToken: 'ficticio', expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'router-real', email: 'test@example.invalid', nombre: 'Muestra' }, empresas: [], currentEmpresaId: 'empresa-ficticia', permisos: ['compras.requisiciones.leer', 'compras.ordenes.leer'] });
  for (const catalogo of ['catalogos/departamentos', 'catalogos/proveedores', 'identidad/usuarios']) {
    mswServer.use(http.get(`*/api/v1/${catalogo}`, () => HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 })));
  }
  mswServer.use(http.get('*/api/v1/compras/requisiciones', ({ request }) => {
    const q = new URL(request.url).searchParams; const limit = Number(q.get('limit')); const offset = Number(q.get('offset'));
    peticiones.push({ tipo: 'rq', cantidad: limit, posicion: offset });
    return HttpResponse.json({ items: Array.from({ length: Math.min(limit, 43 - offset) }, (_, i) => ({ id: `rq-${offset+i}`, folio: `RQ-MUESTRA-${offset+i}`, folioAnio: 2026, estado: 0, clasificacion: 0, prioridad: 0, sucursalId: 's', departamentoId: 'd', requisitanteId: 'u', descripcion: null, fechaSolicitud: '2026-11-13T10:00:00Z', fechaEntregaDeseada: null })), offset, limit, total: 43 });
  }));
  mswServer.use(http.get('*/api/v1/compras/ordenes', ({ request }) => {
    const q = new URL(request.url).searchParams; const pageSize = Number(q.get('pageSize')); const page = Number(q.get('page'));
    peticiones.push({ tipo: 'oc', cantidad: pageSize, posicion: page });
    return HttpResponse.json({ items: Array.from({ length: Math.min(pageSize, 43 - (page-1)*pageSize) }, (_, i) => ({ id: `oc-${i}`, folio: `OC-MUESTRA-${i}`, folioAnio: 2026, estado: 0, subEstadoRecepcion: 0, subEstadoFacturacion: 0, subEstadoPago: 0, proveedorId: 'p', compradorTitularId: 'u', moneda: 'MXN', fechaDocumento: '2026-11-13', referenciaProveedor: null })), page, pageSize, totalCount: 43 });
  }));
});
afterEach(() => { cleanup(); cerrar?.(); localStorage.clear(); window.history.replaceState({}, '', '/'); useAuthStore.setState({ user: null, status: 'idle', accessToken: null, permisos: [] }); });
async function iniciar() {
  const history = createBrowserHistory(); cerrar = () => history.destroy();
  const router = createRouter({ routeTree: tree, history, defaultPreload: false, defaultPendingMinMs: 0 });
  await router.load(); const Wrapper = createQueryWrapper();
  render(<Wrapper><NuevaOrdenCompraContext.Provider value={noop}><RouterProvider router={router} /></NuevaOrdenCompraContext.Provider></Wrapper>);
  return router;
}
it('recuerda20 en RQ y OC al navegar por SPA sin cantidad y conserva el enlace explícito10/página2', async () => {
  const router = await iniciar();
  await screen.findByText('1–10 de 43');
  fireEvent.change(screen.getByLabelText('Filas por página'), { target: { value: '20' } });
  await screen.findByText('1–20 de 43');
  await act(async () => { await router.navigate({ to: '/compras/ordenes' }); });
  await screen.findByText('1–10 de 43');
  fireEvent.change(screen.getByLabelText('Filas por página'), { target: { value: '20' } });
  await screen.findByText('1–20 de 43');
  await act(async () => { await router.navigate({ to: '/compras/requisiciones' }); });
  await waitFor(() => expect(screen.getByLabelText('Filas por página')).toHaveValue('20'));
  await screen.findByText('1–20 de 43');
  expect(new URLSearchParams(window.location.search).has('limit')).toBe(false);
  await act(async () => { await router.navigate({ to: '/compras/ordenes' }); });
  await waitFor(() => expect(screen.getByLabelText('Filas por página')).toHaveValue('20'));
  await screen.findByText('1–20 de 43');
  expect(new URLSearchParams(window.location.search).has('pageSize')).toBe(false);
  await act(async () => { await router.navigate({ to: '/compras/ordenes', search: { page: 2, pageSize: 10 } }); });
  await screen.findByText('11–20 de 43');
  expect(screen.getByLabelText('Filas por página')).toHaveValue('10');
  expect(peticiones.at(-1)).toMatchObject({ tipo: 'oc', cantidad: 10, posicion: 2 });
  await act(async () => { await router.navigate({ to: '/compras/requisiciones', search: { offset: 10, limit: 10 } }); });
  await screen.findByText('11–20 de 43');
  expect(peticiones.at(-1)).toMatchObject({ tipo: 'rq', cantidad: 10, posicion: 10 });
});
