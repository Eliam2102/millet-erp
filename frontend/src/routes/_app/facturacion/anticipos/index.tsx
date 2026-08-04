import { createFileRoute, redirect } from '@tanstack/react-router';
import { ControlAnticipos } from '@/features/facturacion/pages/ControlAnticipos';
import {
  AnticiposSearchSchema,
  type AnticiposSearch,
} from '@/features/facturacion/lib/anticipos-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Control de Anticipos (resumen) — <c>/facturacion/anticipos</c> (FE-F4-PR2).
 */
export const Route = createFileRoute('/_app/facturacion/anticipos/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.FacturacionAnticiposLeer)) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: ControlAnticipos,
  validateSearch: (input: Record<string, unknown>): AnticiposSearch =>
    AnticiposSearchSchema.parse(input),
});
