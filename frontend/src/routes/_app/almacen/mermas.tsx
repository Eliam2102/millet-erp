import { createFileRoute, redirect } from '@tanstack/react-router';
import { MermasProcesoPage } from '@/features/almacen/pages/MermasProcesoPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta A4.4 — Desperdicio de mesa de corte y merma por etapa.
 * Gate: <c>almacen.salidas.leer-todas</c> (lectura); autorizar pide <c>almacen.mermas.autorizar</c>.
 */
export const Route = createFileRoute('/_app/almacen/mermas')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenSalidasLeerTodas)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: MermasProcesoPage,
});
