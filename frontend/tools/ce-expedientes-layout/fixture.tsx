import { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  RouterProvider,
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
} from '@tanstack/react-router';
import { ModulePanel } from '../../src/components/layout/ModulePanel';
import { contextoNavegacion } from '../../src/lib/nav';
import { ConflictDialogProvider } from '../../src/components/erp/collaboration/ConflictDialogProvider';
import { useAuthStore } from '../../src/lib/auth/auth-store';
import { PermisosCanonicos as P } from '../../src/lib/auth/permission-codes';
import { ExpedienteDetallePage } from '../../src/features/comercio-exterior/pages/ExpedienteDetallePage';
import { muestra } from './muestra';
import './layout.css';
const params = new URLSearchParams(location.search);
const data = structuredClone(muestra);
if (params.has('largo')) {
  data.expediente.referencia = 'MUESTRA-FICTICIA-REFERENCIA-LARGA-'.repeat(6);
  data.expediente.responsableNombre =
    'Responsable de documentación y seguimiento de operaciones internacionales de muestra';
  data.ordenesCompra = [1, 2, 3].map((i) => ({
    id: `oc-${i}`,
    folio: `OC-MUESTRA2026-00000${i}`,
    moneda: 'USD',
    sucursalId: 'conkal',
    informacionImportacion: null,
  }));
  data.ocId = 'oc-1';
  data.ocFolio = 'OC-MUESTRA2026-000001';
  data.informacionImportacion = {
    paisOrigen: 'País de origen de muestra',
    numeroContenedor: 'CONTENEDOR-MUESTRA-123456789',
    codigoRuta: 'RUTA-MUESTRA-DE-LARGA-DISTANCIA',
    numeroPedimento: '26-00-0000-0000001',
  };
}
const permisos = params.has('lectura') ? [P.ComercioExteriorExpedientesLeer] : Object.values(P);
useAuthStore.setState({
  status: 'authenticated',
  accessToken: 'muestra-ficticia',
  expiresAt: new Date('2099-01-01'),
  user: { id: 'muestra', nombre: 'Muestra ficticia', email: 'muestra@example.invalid' },
  currentEmpresaId: 'muestra',
  permisos,
});
// La fixture nunca conecta con el ERP: toda consulta es ficticia y toda escritura se rechaza.
window.fetch = async (input, init) => {
  const url = new URL(
    typeof input === 'string' ? input : input instanceof URL ? input.href : input.url,
    location.origin,
  );
  if ((init?.method ?? 'GET') !== 'GET') return new Response('{}', { status: 405 });
  const p = url.pathname;
  let body: unknown = [];
  if (p.endsWith('/expedientes'))
    body = {
      items: [data.expediente, { ...data.expediente, id: 'otro', folio: 'CE-EXP-2026-000002' }],
      total: 2,
      offset: 0,
      limit: 10,
    };
  else if (p.endsWith('/ce-muestra')) body = data;
  else if (p.endsWith('/contenedores'))
    body = {
      contenedores: [],
      partidas: [],
      pedidos: [],
      aduanas: {},
      pedimento: null,
      version: 1,
    };
  else if (p.endsWith('/finanzas'))
    body = {
      estadoFinanciero: 'Listo para cierre',
      version: 1,
      nombres: {},
      pendientes: [],
      ordenesServicio: [],
      conceptos: [],
      gastos: [],
      partidas: [],
      bases: [],
      distribuciones: [],
      anticipos: [],
    };
  else if (p.endsWith('/bitacora')) body = { items: [] };
  else if (p.includes('otr')) body = [];
  else if (p.includes('incoterm'))
    body = [{ id: 'ddp', codigo: 'DDP', nombre: 'Delivered Duty Paid', activo: true }];
  else if (p.includes('monedas'))
    body = [{ id: 'USD', codigo: 'USD', nombre: 'Dólar estadounidense', activo: true }];
  return new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } });
};
export function Shell() {
  const [panel, setPanel] = useState(true);
  const modulo = contextoNavegacion('/comercio-exterior/expedientes/ce-muestra', permisos)!.modulo;
  return (
    <div className="min-h-screen bg-surface-page text-ink">
      <div className="fixed inset-y-0 left-0 hidden w-rail bg-rail md:block" />
      <div className="md:ml-rail md:flex">
        {panel && <ModulePanel modulo={modulo} onCollapse={() => setPanel(false)} />}
        <div className="min-w-0 flex-1">
          <div className="flex h-topbar items-center border-b border-line bg-surface-card px-4 text-sm">
            Comercio Exterior · MUESTRA FICTICIA · revisión local sin conexión
          </div>
          <main className="relative min-h-[calc(100vh-3.5rem)] px-4 pt-6 pb-10 md:px-6">
            <Outlet />
          </main>
        </div>
      </div>
    </div>
  );
}
const root = createRootRoute({ component: Shell });
const detalle = createRoute({
  getParentRoute: () => root,
  path: '/comercio-exterior/expedientes/$id',
  component: () => <ExpedienteDetallePage id="ce-muestra" />,
});
const router = createRouter({
  routeTree: root.addChildren([detalle]),
  history: createMemoryHistory({ initialEntries: ['/comercio-exterior/expedientes/ce-muestra'] }),
});
const client = new QueryClient({
  defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
});
createRoot(document.getElementById('root')!).render(
  <QueryClientProvider client={client}>
    <ConflictDialogProvider>
      <RouterProvider router={router} />
    </ConflictDialogProvider>
  </QueryClientProvider>,
);
