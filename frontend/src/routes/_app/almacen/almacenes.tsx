import { createFileRoute, redirect } from '@tanstack/react-router';
import { AlmacenesPage } from '@/features/almacen/pages/AlmacenesPage';
import {
  AlmacenesSearchSchema,
  type AlmacenesSearch,
} from '@/features/almacen/lib/catalogo-search-schemas';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P1 — Bandeja de almacenes (doc 07 §FE-F1-PR1).
 * Gate: <c>almacen.almacenes.leer</c>. Sin permiso, redirige a la
 * landing del módulo (que muestra solo lo que el usuario puede ver).
 */
export const Route = createFileRoute('/_app/almacen/almacenes')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenAlmacenesRead)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: AlmacenesPage,
  validateSearch: (input: Record<string, unknown>): AlmacenesSearch =>
    AlmacenesSearchSchema.parse(input),
});
