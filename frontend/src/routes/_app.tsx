import { createFileRoute, Outlet, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { rutaPermitida } from '@/lib/nav';
import { AppShell } from '@/components/layout/AppShell';
import { ConflictDialogProvider } from '@/components/erp/collaboration/ConflictDialogProvider';
import { NuevaRequisicionProvider } from '@/features/compras/components/NuevaRequisicionProvider';
import { NuevaOrdenCompraProvider } from '@/features/compras/ordenes/components/NuevaOrdenCompraProvider';
import { NuevoPedidoProvider } from '@/features/facturacion/components/NuevoPedidoProvider';
import { NuevoAnticipoProvider } from '@/features/facturacion/components/NuevoAnticipoProvider';
import { NuevoReppProvider } from '@/features/facturacion/components/NuevoReppProvider';
import { NuevaCartaPorteProvider } from '@/features/facturacion/components/NuevaCartaPorteProvider';
import { NuevaCajaProvider } from '@/features/facturacion/components/NuevaCajaProvider';
import { NuevaLineaCreditoProvider } from '@/features/cxc/components/NuevaLineaCreditoProvider';
import { RegistrarGestionProvider } from '@/features/cxc/components/RegistrarGestionProvider';
import { NuevaPropuestaProvider } from '@/features/cxc/components/NuevaPropuestaProvider';
import { Toaster } from '@/components/ui/sonner';

/**
 * Layout pathless <c>_app</c> — todas las rutas del ERP autenticado viven
 * bajo este layout y heredan el AppShell (sidebar + topbar). El guard de
 * autenticación se centraliza aquí, así una nueva ruta protegida solo
 * necesita crearse como <c>routes/_app/&lt;modulo&gt;.tsx</c> sin repetir
 * la lógica de redirect.
 *
 * Login NO está bajo _app → no ve el shell.
 */
export const Route = createFileRoute('/_app')({
  beforeLoad: ({ location }) => {
    const { status, permisos } = useAuthStore.getState();
    if (status !== 'authenticated') {
      throw redirect({ to: '/login' });
    }
    // Misma regla que el sidebar: una URL de un módulo o pantalla sin
    // permiso regresa a Inicio en lugar de mostrar una pantalla vacía o 403.
    if (!rutaPermitida(location.pathname, permisos)) {
      throw redirect({ to: '/' });
    }
  },
  component: AppLayout,
});

function AppLayout() {
  return (
    <ConflictDialogProvider>
      <NuevaRequisicionProvider>
        <NuevaOrdenCompraProvider>
          <NuevoPedidoProvider>
              <NuevoAnticipoProvider>
              <NuevoReppProvider>
              <NuevaCartaPorteProvider>
              <NuevaCajaProvider>
              <NuevaLineaCreditoProvider>
              <RegistrarGestionProvider>
              <NuevaPropuestaProvider>
              <AppShell>
                <Outlet />
                {/* <c>&lt;Toaster/&gt;</c> vive una sola vez bajo el shell para que toda
                    ruta autenticada pueda llamar <c>toast(...)</c> sin re-instanciarlo.
                    La pantalla de login NO está bajo _app, así que no recibe toasts —
                    es deliberado: el flujo de auth maneja sus mensajes inline. */}
                <Toaster richColors position="top-right" closeButton />
              </AppShell>
              </NuevaPropuestaProvider>
              </RegistrarGestionProvider>
              </NuevaLineaCreditoProvider>
              </NuevaCajaProvider>
              </NuevaCartaPorteProvider>
              </NuevoReppProvider>
              </NuevoAnticipoProvider>
          </NuevoPedidoProvider>
        </NuevaOrdenCompraProvider>
      </NuevaRequisicionProvider>
    </ConflictDialogProvider>
  );
}
