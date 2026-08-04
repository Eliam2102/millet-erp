import { createFileRoute, redirect } from '@tanstack/react-router';
import { SaldosPage } from '@/features/almacen/pages/SaldosPage';
import {
  SaldosSearchSchema,
  type SaldosSearch,
} from '@/features/almacen/lib/saldos-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P11 — Saldos materializados (doc 07 §FE-F6-PR1).
 * Gate: <c>almacen.almacenes.leer</c>.
 */
export const Route = createFileRoute('/_app/almacen/saldos')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenAlmacenesRead)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: SaldosPage,
  validateSearch: (input: Record<string, unknown>): SaldosSearch =>
    SaldosSearchSchema.parse(input),
});
