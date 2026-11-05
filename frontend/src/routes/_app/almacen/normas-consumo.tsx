import { createFileRoute, redirect } from '@tanstack/react-router';
import { NormasConsumoPage } from '@/features/almacen/pages/NormasConsumoPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta A4.8 — Catálogo de normas de consumo de componentes (edición con <c>almacen.almacenes.administrar</c>).
 * Gate: <c>almacen.salidas.leer-todas</c>.
 */
export const Route = createFileRoute('/_app/almacen/normas-consumo')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenSalidasLeerTodas)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: NormasConsumoPage,
});
