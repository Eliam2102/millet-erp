import { createFileRoute, redirect } from '@tanstack/react-router';
import { RecepcionesPage } from '@/features/almacen/pages/RecepcionesPage';
import {
  RecepcionesSearchSchema,
  type RecepcionesSearch,
} from '@/features/almacen/lib/recepciones-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P1 — Bandeja de recepciones (doc 07 §FE-F2-PR1).
 * Gate: <c>almacen.entradas.leer</c>. Sin permiso, redirige a la
 * landing del módulo (que muestra solo las cards permitidas).
 */
export const Route = createFileRoute('/_app/almacen/recepciones/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenEntradasLeer)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: RecepcionesPage,
  validateSearch: (input: Record<string, unknown>): RecepcionesSearch =>
    RecepcionesSearchSchema.parse(input),
});
