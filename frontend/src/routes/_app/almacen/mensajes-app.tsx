import { createFileRoute, redirect } from '@tanstack/react-router';
import { MensajesAppPage } from '@/features/almacen/pages/MensajesAppPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Entradas y salidas de la app de Almacenes marcadas por el ERP (sin existencia o con un dato por corregir).
 * Gate: <c>almacen.salidas.leer-todas</c>, el mismo de la bandeja que la alimenta.
 */
export const Route = createFileRoute('/_app/almacen/mensajes-app')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenSalidasLeerTodas)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: MensajesAppPage,
});
