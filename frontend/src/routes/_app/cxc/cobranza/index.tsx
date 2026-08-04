import { createFileRoute, redirect } from '@tanstack/react-router';
import { CobranzaPage } from '@/features/cxc/pages/CobranzaPage';
import {
  CobranzaSearchSchema,
  type CobranzaSearch,
} from '@/features/cxc/lib/cobranza-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Cobranza — <c>/cxc/cobranza</c> (CXC-FE-PR4, P2 con filtro de cliente
 * obligatorio). Lectura gateada por <c>cartera.leer</c> (espejo del GET
 * del backend); <c>cobranza.registrar</c> habilita el sheet "Registrar
 * gestión".
 */
export const Route = createFileRoute('/_app/cxc/cobranza/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    const permitido = [
      PermisosCanonicos.CuentasPorCobrarCarteraLeer,
      PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar,
    ].some((p) => permisos.includes(p));
    if (!permitido) {
      throw redirect({ to: '/cxc' });
    }
  },
  component: CobranzaPage,
  validateSearch: (input: Record<string, unknown>): CobranzaSearch =>
    CobranzaSearchSchema.parse(input),
});
