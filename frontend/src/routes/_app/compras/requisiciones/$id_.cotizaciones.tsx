import { createFileRoute, redirect } from '@tanstack/react-router';
import { ComparativoCotizaciones } from '@/features/compras/pages/ComparativoCotizaciones';
import { DEFAULT_BANDEJA_SEARCH } from '@/features/compras/lib/bandeja-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * F1-COM-02 — Comparativo de cotizaciones de una requisición. Ruta sin anidar bajo
 * el detalle (`$id_`) para ocupar el área completa. Gate: `compras.cotizaciones.leer`.
 */
export const Route = createFileRoute('/_app/compras/requisiciones/$id_/cotizaciones')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.ComprasCotizacionesLeer)) {
      throw redirect({ to: '/compras/requisiciones', search: DEFAULT_BANDEJA_SEARCH });
    }
  },
  component: ComparativoCotizaciones,
});
