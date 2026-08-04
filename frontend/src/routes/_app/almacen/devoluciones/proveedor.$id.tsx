import { createFileRoute, redirect } from '@tanstack/react-router';
import { DevolucionProveedorDetallePage } from '@/features/almacen/pages/DevolucionProveedorDetallePage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P6 — Detalle de devolución a proveedor (doc 07 §FE-F4-PR1).
 * Gate: cualquiera de los permisos de devolución a proveedor o el
 * de leer-internas (un Almacenista puede mirar sin poder accionar).
 */
export const Route = createFileRoute(
  '/_app/almacen/devoluciones/proveedor/$id',
)({
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
  component: DevolucionProveedorDetallePage,
});
