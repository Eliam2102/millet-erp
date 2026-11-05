import { createFileRoute, redirect } from '@tanstack/react-router';
import { ConsumosRevisionPage } from '@/features/almacen/pages/ConsumosRevisionPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta A4.8 — Bandeja de revisión de consumos de componentes.
 * Gate: <c>almacen.salidas.leer-todas</c>.
 */
export const Route = createFileRoute('/_app/almacen/consumos-revision')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenSalidasLeerTodas)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: ConsumosRevisionPage,
});
