import { createFileRoute, redirect } from '@tanstack/react-router';
import { BandejaRepp } from '@/features/facturacion/pages/BandejaRepp';
import {
  ReppSearchSchema,
  type ReppSearch,
} from '@/features/facturacion/lib/repp-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Bandeja de complementos de pago (REPP) — <c>/facturacion/repp</c> (FE-F6).
 */
export const Route = createFileRoute('/_app/facturacion/repp/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    const permitido = [
      PermisosCanonicos.FacturacionReppEmitir,
      PermisosCanonicos.FacturacionFacturasLeer,
    ].some((p) => permisos.includes(p));
    if (!permitido) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: BandejaRepp,
  validateSearch: (input: Record<string, unknown>): ReppSearch =>
    ReppSearchSchema.parse(input),
});
