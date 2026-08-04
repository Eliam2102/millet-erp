import { createFileRoute, redirect } from '@tanstack/react-router';
import { DevolucionesPage } from '@/features/almacen/pages/DevolucionesPage';
import {
  DevolucionesSearchSchema,
  type DevolucionesSearch,
} from '@/features/almacen/lib/devoluciones-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P5 — Bandeja unificada de devoluciones (doc 07 §FE-F4-PR1).
 * Gate: <c>almacen.devoluciones-internas.leer</c> O cualquiera de los
 * permisos de devolución a proveedor.
 */
export const Route = createFileRoute('/_app/almacen/devoluciones/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    const puedeVer =
      permisos.includes(PermisosCanonicos.AlmacenDevolucionesInternasLeer) ||
      permisos.includes(
        PermisosCanonicos.AlmacenDevolucionesProveedorIniciar,
      ) ||
      permisos.includes(
        PermisosCanonicos.AlmacenDevolucionesProveedorAutorizar,
      ) ||
      permisos.includes(
        PermisosCanonicos.AlmacenDevolucionesProveedorRegistrar,
      );
    if (!puedeVer) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: DevolucionesPage,
  validateSearch: (input: Record<string, unknown>): DevolucionesSearch =>
    DevolucionesSearchSchema.parse(input),
});
