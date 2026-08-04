import { createFileRoute, redirect } from '@tanstack/react-router';
import { ReordenPage } from '@/features/almacen/pages/ReordenPage';
import {
  ReordenSearchSchema,
  type ReordenSearch,
} from '@/features/almacen/lib/catalogo-search-schemas';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta de la bandeja de reabasto (código = reorden, ADR-0047 PR5.F).
 * Gate: <c>almacen.reorden.leer</c>. Sin permiso, redirige a la landing del
 * módulo. La ruta técnica es <c>/almacen/reorden</c>; la UI dice "Reabasto".
 */
export const Route = createFileRoute('/_app/almacen/reorden')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenReordenRead)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: ReordenPage,
  validateSearch: (input: Record<string, unknown>): ReordenSearch =>
    ReordenSearchSchema.parse(input),
});
