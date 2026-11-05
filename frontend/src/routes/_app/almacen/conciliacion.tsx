import { createFileRoute, redirect } from '@tanstack/react-router';
import { ConciliacionDiariaPage } from '@/features/almacen/pages/ConciliacionDiariaPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta A4.6 — Conciliación diaria del ERP contra A+W.
 * Gate: <c>almacen.inventarios.leer</c>; correr y aclarar piden <c>almacen.inventarios.crear</c>.
 */
export const Route = createFileRoute('/_app/almacen/conciliacion')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenInventariosLeer)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: ConciliacionDiariaPage,
});
