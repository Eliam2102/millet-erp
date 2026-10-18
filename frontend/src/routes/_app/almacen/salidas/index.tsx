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
 * Gate: <c>almacen.salidas.leer-todas</c>. El backend no filtra "propias"
 * para salidas, por lo que el permiso <c>leer-propias</c> no basta.
 * Sin el permiso, redirige a la landing.
 */
export const Route = createFileRoute('/_app/almacen/salidas/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    const puedeVer = permisos.includes(
      PermisosCanonicos.AlmacenSalidasLeerTodas,
    );
    if (!puedeVer) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: SalidasPage,
  validateSearch: (input: Record<string, unknown>): SalidasSearch =>
    SalidasSearchSchema.parse(input),
});
