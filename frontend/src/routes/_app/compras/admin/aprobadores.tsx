import { createFileRoute, redirect } from '@tanstack/react-router';
import { AdminAprobadores } from '@/features/compras/pages/AdminAprobadores';
import {
  AdminAprobadoresSearchSchema,
  type AdminAprobadoresSearch,
} from '@/features/compras/lib/admin-aprobadores-search-schema';
import { DEFAULT_BANDEJA_SEARCH } from '@/features/compras/lib/bandeja-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P9 — Admin de aprobadores (doc 05 §11.4).
 *
 * <para><c>beforeLoad</c> gateaa nivel ruta: sin
 * <c>compras.aprobadores.administrar</c>, redirige a la bandeja P1
 * (mejor que mostrar 403 desde el componente — el usuario nunca
 * vería esa pantalla a menos que escriba la URL directa).</para>
 *
 * <para><c>validateSearch</c> aplica los defaults del Zod schema y
 * filtra valores inválidos.</para>
 */
export const Route = createFileRoute('/_app/compras/admin/aprobadores')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.ComprasAprobadoresAdministrar)
    ) {
      throw redirect({
        to: '/compras/requisiciones',
        search: DEFAULT_BANDEJA_SEARCH,
      });
    }
  },
  component: AdminAprobadores,
  validateSearch: (input: Record<string, unknown>): AdminAprobadoresSearch =>
    AdminAprobadoresSearchSchema.parse(input),
});
