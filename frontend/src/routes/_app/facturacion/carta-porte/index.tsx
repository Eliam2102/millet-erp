import { createFileRoute, redirect } from '@tanstack/react-router';
import { BandejaCartaPorte } from '@/features/facturacion/pages/BandejaCartaPorte';
import {
  CartaPorteSearchSchema,
  type CartaPorteSearch,
} from '@/features/facturacion/lib/carta-porte-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Bandeja de Carta Porte 3.1 — <c>/facturacion/carta-porte</c> (FE-F8).
 */
export const Route = createFileRoute('/_app/facturacion/carta-porte/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.FacturacionCartaPorteLeer)) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: BandejaCartaPorte,
  validateSearch: (input: Record<string, unknown>): CartaPorteSearch =>
    CartaPorteSearchSchema.parse(input),
});
