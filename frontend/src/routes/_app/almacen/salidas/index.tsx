import { createFileRoute, redirect } from '@tanstack/react-router';
import { SalidasPage } from '@/features/almacen/pages/SalidasPage';
import {
  SalidasSearchSchema,
  type SalidasSearch,
} from '@/features/almacen/lib/salidas-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P3 — Bandeja de salidas (doc 07 §FE-F3-PR1).
 * Gate: <c>almacen.salidas.leer-todas</c> O <c>almacen.salidas.leer-propias</c>.
 * Sin ninguno, redirige a la landing.
 */
export const Route = createFileRoute('/_app/almacen/salidas/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    const puedeVer =
      permisos.includes(PermisosCanonicos.AlmacenSalidasLeerTodas) ||
      permisos.includes(PermisosCanonicos.AlmacenSalidasLeerPropias);
    if (!puedeVer) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: SalidasPage,
  validateSearch: (input: Record<string, unknown>): SalidasSearch =>
    SalidasSearchSchema.parse(input),
});
