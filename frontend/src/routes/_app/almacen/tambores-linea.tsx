import { createFileRoute, redirect } from '@tanstack/react-router';
import { TamboresLineaPage } from '@/features/almacen/pages/TamboresLineaPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/** Ruta M-172 — Tambores por línea de producción. Gate: <c>almacen.salidas.leer-todas</c>. */
export const Route = createFileRoute('/_app/almacen/tambores-linea')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenSalidasLeerTodas)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: TamboresLineaPage,
});
