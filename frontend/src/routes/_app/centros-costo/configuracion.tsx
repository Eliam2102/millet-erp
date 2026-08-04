import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ConfiguracionCentrosCostoPage } from '@/features/centros-costo/pages/ConfiguracionCentrosCostoPage';

/**
 * <c>/centros-costo/configuracion</c> — Módulo 1 del 05 §2: árbol de
 * configuración de 3 niveles (grupos como chips). CECO-FE-PR1 lo entrega
 * read-only; el CRUD con modales llega en FE-PR2.
 *
 * <para>Guard: lectura del catálogo con <c>catalogo.leer</c>; si el rol
 * solo trae <c>catalogo.administrar</c> sin lectura, igual pasa —
 * administrar implica leer en la práctica (mismo criterio que el guard
 * de monedas).</para>
 */
export const Route = createFileRoute('/_app/centros-costo/configuracion')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.CentrosCostoCatalogoLeer) &&
      !permisos.includes(PermisosCanonicos.CentrosCostoCatalogoAdministrar)
    ) {
      throw redirect({ to: '/' });
    }
  },
  component: ConfiguracionCentrosCostoPage,
});
