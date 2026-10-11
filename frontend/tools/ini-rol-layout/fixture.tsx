import React from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider, createMemoryHistory, createRootRoute, createRoute, createRouter, Outlet } from '@tanstack/react-router';
import { useAuthStore } from '../../src/lib/auth/auth-store';
import { PermisosCanonicos as P } from '../../src/lib/auth/permission-codes';
import { PanelInicio } from '../../src/features/inicio/components/PanelInicio';
import { NuevaRequisicionContext } from '../../src/features/compras/components/nueva-requisicion-context';
import { NuevaOrdenCompraContext } from '../../src/features/compras/ordenes/components/nueva-orden-compra-context';
import { RegistrarGestionContext } from '../../src/features/cxc/components/registrar-gestion-context';
import './layout.css';
const noop = () => {};
const perfil = new URLSearchParams(location.search).get('perfil');
const roles = {
 compras: [P.ComprasRequisicionesLeer,P.ComprasOrdenesLeer,P.ComprasRequisicionesCrear],
 almacen: [P.AlmacenEntradasLeer,P.AlmacenSalidasLeerTodas,P.AlmacenInventariosLeer],
 cobranza: [P.CuentasPorCobrarCarteraLeer,P.TesoreriaDepositosConfirmar],
 tesoreria: [P.TesoreriaCuentasVer,P.TesoreriaPasivosVer,P.TesoreriaMovimientosVer,P.TesoreriaDepositosConfirmar],
 admin: Object.values(P),
};
useAuthStore.setState({status:'authenticated',accessToken:'fixture-sin-login',user:{id:'fixture',nombre:'Ana · muestra ficticia',email:'fixture@example.test'},currentEmpresaId:'fixture',empresas:[{id:'fixture',razonSocial:'Muestra ficticia · no conectada al ERP',rfc:'DEMO',esLaActual:true}],permisos: perfil && perfil in roles ? roles[perfil as keyof typeof roles] : roles.admin});
const cliente = new QueryClient({defaultOptions:{queries:{retry:false}}});
const raiz = createRootRoute({component:()=> <><div className="fixed inset-y-0 left-0 hidden w-rail bg-rail md:block"/><div className="md:ml-rail"><div className="flex h-14 items-center bg-surface-card px-6">Inicio · verificación aislada con muestras</div><main><Outlet/></main></div></>});
const inicio = createRoute({getParentRoute:()=>raiz,path:'/',component:PanelInicio});
const router = createRouter({routeTree:raiz.addChildren([inicio]),history:createMemoryHistory({initialEntries:['/']})});
createRoot(document.getElementById('root')!).render(<React.StrictMode><QueryClientProvider client={cliente}>
<NuevaRequisicionContext.Provider value={{abrir:noop,cerrar:noop,setDirty:noop}}><NuevaOrdenCompraContext.Provider value={{abrir:noop,cerrar:noop,setDirty:noop}}>
<RegistrarGestionContext.Provider value={{abrir:noop,cerrar:noop,setDirty:noop}}><RouterProvider router={router}/></RegistrarGestionContext.Provider>
</NuevaOrdenCompraContext.Provider></NuevaRequisicionContext.Provider></QueryClientProvider></React.StrictMode>);
