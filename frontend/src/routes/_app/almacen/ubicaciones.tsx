import { createFileRoute, redirect } from '@tanstack/react-router';
import { UbicacionesPage } from '@/features/almacen/pages/UbicacionesPage';
import {
  UbicacionesSearchSchema,
  type UbicacionesSearch,
} from '@/features/almacen/lib/catalogo-search-schemas';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P2 — Bandeja de ubicaciones N4 (racks/pasillos, ADR-0047 PR C7.1).
 * Gate: <c>almacen.ubicaciones.leer</c>. Sin permiso, redirige a la landing.
 */
export const Route = createFileRoute('/_app/almacen/ubicaciones')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenUbicacionesRead)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: UbicacionesPage,
  validateSearch: (input: Record<string, unknown>): UbicacionesSearch =>
    UbicacionesSearchSchema.parse(input),
});
