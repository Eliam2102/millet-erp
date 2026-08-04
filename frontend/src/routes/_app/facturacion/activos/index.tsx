import { createFileRoute, redirect } from '@tanstack/react-router';
import { Activos } from '@/features/facturacion/pages/Activos';
import {
  ActivosSearchSchema,
  type ActivosSearch,
} from '@/features/facturacion/lib/activos-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Autorizaciones de venta de activo — <c>/facturacion/activos</c> (FE-F9).
 * Bandeja del Contador General.
 */
export const Route = createFileRoute('/_app/facturacion/activos/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.FacturacionActivosAutorizar)) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: Activos,
  validateSearch: (input: Record<string, unknown>): ActivosSearch =>
    ActivosSearchSchema.parse(input),
});
