import { createFileRoute, redirect } from '@tanstack/react-router';
import { BandejaFacturasAnticipo } from '@/features/facturacion/pages/BandejaFacturasAnticipo';
import {
  FacturasAnticipoSearchSchema,
  type FacturasAnticipoSearch,
} from '@/features/facturacion/lib/facturas-anticipo-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Bandeja de facturas de anticipo —
 * <c>/facturacion/anticipos/facturas</c> (ANT-PR2, doc 13 §6). El segmento
 * estático <c>facturas</c> gana al dinámico <c>$clienteId</c> del estado de
 * cuenta, así que ambas rutas conviven bajo <c>/anticipos</c>.
 */
export const Route = createFileRoute('/_app/facturacion/anticipos/facturas/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.FacturacionAnticiposLeer)) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: BandejaFacturasAnticipo,
  validateSearch: (input: Record<string, unknown>): FacturasAnticipoSearch =>
    FacturasAnticipoSearchSchema.parse(input),
});
