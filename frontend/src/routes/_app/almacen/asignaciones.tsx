import { createFileRoute, redirect } from '@tanstack/react-router';
import { AsignacionesPage } from '@/features/almacen/pages/AsignacionesPage';
import {
  AsignacionesSearchSchema,
  type AsignacionesSearch,
} from '@/features/almacen/lib/catalogo-search-schemas';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta de "Ubicación de artículos" (asignación N4, ADR-0047 PR C).
 * Gate: <c>almacen.asignaciones.leer</c>. Sin permiso, redirige a la landing del
 * módulo. Ruta técnica <c>/almacen/asignaciones</c>; la UI dice "Ubicación de
 * artículos".
 */
export const Route = createFileRoute('/_app/almacen/asignaciones')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenAsignacionesRead)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: AsignacionesPage,
  validateSearch: (input: Record<string, unknown>): AsignacionesSearch =>
    AsignacionesSearchSchema.parse(input),
});
