import { createFileRoute, redirect, useParams } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { CorridaAwDetalle } from '@/modules/datos-maestros/components/CorridaAwDetalle';

/**
 * <c>/admin/datos-maestros/sincronizacion-aw/$id</c> — detalle de una corrida de clientes y productos.
 * Guard: requiere <c>datos_maestros.clientes.sincronizar</c>; sin él redirige a <c>/</c>.
 */
export const Route = createFileRoute('/_app/admin/datos-maestros/sincronizacion-aw/$id')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.DatosMaestrosClientesSincronizar)) {
      throw redirect({ to: '/' });
    }
  },
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({ from: '/_app/admin/datos-maestros/sincronizacion-aw/$id' });
  return <CorridaAwDetalle key={id} id={id} />;
}
