import { createFileRoute, redirect } from '@tanstack/react-router';
import { SalidasRoturaPage } from '@/features/almacen/pages/SalidasRoturaPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta ALM-03 — Salida por rotura en almacén.
 * Gate: <c>almacen.salidas.leer-todas</c> y capturar o autorizar rotura. Sin ellos, regresa a la landing.
 */
export const Route = createFileRoute('/_app/almacen/salidas-rotura')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    const puede =
      permisos.includes(PermisosCanonicos.AlmacenSalidasLeerTodas) &&
      (permisos.includes(PermisosCanonicos.AlmacenSalidasRoturaCapturar) ||
        permisos.includes(PermisosCanonicos.AlmacenSalidasRoturaAutorizar));
    if (!puede) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: SalidasRoturaPage,
});
