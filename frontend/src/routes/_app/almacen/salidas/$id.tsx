import { createFileRoute, redirect } from '@tanstack/react-router';
import { SalidaDetallePage } from '@/features/almacen/pages/SalidaDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P4 — Detalle de salida (doc 07 §FE-F3-PR1). Mismo gate que la
 * bandeja: <c>almacen.salidas.leer-todas</c>. El backend no filtra
 * "propias" para salidas.
 */
export const Route = createFileRoute('/_app/almacen/salidas/$id')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    const puedeVer = permisos.includes(
      PermisosCanonicos.AlmacenSalidasLeerTodas,
    );
    if (!puedeVer) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: SalidaDetallePage,
});
