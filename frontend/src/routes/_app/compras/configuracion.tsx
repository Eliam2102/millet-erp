import { createFileRoute, redirect } from '@tanstack/react-router';
import { ConfiguracionCompras } from '@/features/compras/pages/ConfiguracionCompras';
import { DEFAULT_BANDEJA_SEARCH } from '@/features/compras/lib/bandeja-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta <c>/compras/configuracion</c> — destino de la card de Compras en
 * <c>/admin</c>. <c>beforeLoad</c> exige <c>compras.configuracion.leer</c>;
 * sin él redirige a la bandeja de requisiciones.
 */
export const Route = createFileRoute('/_app/compras/configuracion')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.ComprasConfiguracionLeer)) {
      throw redirect({
        to: '/compras/requisiciones',
        search: DEFAULT_BANDEJA_SEARCH,
      });
    }
  },
  component: ConfiguracionCompras,
});
