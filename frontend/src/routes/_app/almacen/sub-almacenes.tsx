import { createFileRoute, redirect } from '@tanstack/react-router';
import { SubAlmacenesPage } from '@/features/almacen/pages/SubAlmacenesPage';
import {
  SubAlmacenesSearchSchema,
  type SubAlmacenesSearch,
} from '@/features/almacen/lib/catalogo-search-schemas';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P2 — Bandeja de sub-almacenes (doc 07 §FE-F1-PR1).
 * Gate: <c>almacen.almacenes.leer</c> (mismo permiso que almacenes;
 * son catálogo del módulo). Sin permiso, redirige a la landing.
 */
export const Route = createFileRoute('/_app/almacen/sub-almacenes')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenAlmacenesRead)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: SubAlmacenesPage,
  validateSearch: (input: Record<string, unknown>): SubAlmacenesSearch =>
    SubAlmacenesSearchSchema.parse(input),
});
