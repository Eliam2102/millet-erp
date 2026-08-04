import { createFileRoute, redirect } from '@tanstack/react-router';
import { BandejaFacturas } from '@/features/facturacion/pages/BandejaFacturas';
import {
  FacturasSearchSchema,
  type FacturasSearch,
} from '@/features/facturacion/lib/facturas-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Bandeja de comprobantes emitidos — <c>/facturacion/facturas</c> (FE-F1-PR2).
 */
export const Route = createFileRoute('/_app/facturacion/facturas/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.FacturacionFacturasLeer)) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: BandejaFacturas,
  validateSearch: (input: Record<string, unknown>): FacturasSearch =>
    FacturasSearchSchema.parse(input),
});
