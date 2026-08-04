import { createFileRoute, redirect } from '@tanstack/react-router';
import { AprobacionConteoPage } from '@/features/almacen/pages/AprobacionConteoPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P10 — Aprobación de conteo (doc 07 §FE-F5-PR2).
 * Gate: <c>almacen.inventarios.aprobar-nivel1</c> O superior.
 *
 * <para>Aislada del rol contador (que NO tiene aprobar-*). Esa
 * separación de permisos es la que garantiza la captura sin sesgo
 * (A6) — el contador no puede llegar a esta pantalla.</para>
 */
export const Route = createFileRoute(
  '/_app/almacen/inventarios/$id/aprobacion',
)({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    const puedeAprobar =
      permisos.includes(PermisosCanonicos.AlmacenInventariosAprobarNivel1) ||
      permisos.includes(PermisosCanonicos.AlmacenInventariosAprobarNivel2) ||
      permisos.includes(PermisosCanonicos.AlmacenInventariosAprobarNivel3);
    if (!puedeAprobar) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: AprobacionConteoPage,
});
