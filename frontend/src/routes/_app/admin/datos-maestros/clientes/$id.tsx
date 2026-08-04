import { createFileRoute, redirect, useParams } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ClientesLayout } from '@/modules/datos-maestros/components/ClientesLayout';
import { ClienteDetalle } from '@/modules/datos-maestros/components/ClienteDetalle';
import { NuevoClienteProvider } from '@/modules/datos-maestros/components/SheetNuevoCliente';

/**
 * <c>/admin/datos-maestros/clientes/$id</c> — detalle dentro del
 * layout master-detail (ADR-0048). <c>key={id}</c> fuerza re-mount al
 * navegar entre dos clientes para evitar estado stale.
 */
export const Route = createFileRoute(
  '/_app/admin/datos-maestros/clientes/$id',
)({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.DatosMaestrosClientesGestionar)
    ) {
      throw redirect({ to: '/' });
    }
  },
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({
    from: '/_app/admin/datos-maestros/clientes/$id',
  });
  return (
    <NuevoClienteProvider>
      <ClientesLayout idActivo={id} detalle={<ClienteDetalle key={id} />} />
    </NuevoClienteProvider>
  );
}
