import { createFileRoute, redirect } from '@tanstack/react-router';
import { SaldosJerarquiaPage } from '@/features/almacen/pages/SaldosJerarquiaPage';
import {
  SaldosJerarquiaSearchSchema,
  type SaldosJerarquiaSearch,
} from '@/features/almacen/lib/saldos-jerarquia-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta de la consulta jerárquica de saldos (PR6, ADR-0047).
 * Gate: <c>almacen.almacenes.leer</c>.
 */
export const Route = createFileRoute('/_app/almacen/saldos-jerarquia')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenAlmacenesRead)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: SaldosJerarquiaPage,
  validateSearch: (input: Record<string, unknown>): SaldosJerarquiaSearch =>
    SaldosJerarquiaSearchSchema.parse(input),
});
