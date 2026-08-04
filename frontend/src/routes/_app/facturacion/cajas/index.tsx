import { createFileRoute, redirect } from '@tanstack/react-router';
import { BandejaCajas } from '@/features/facturacion/pages/BandejaCajas';
import {
  CajasSearchSchema,
  type CajasSearch,
} from '@/features/facturacion/lib/cajas-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Bandeja de cajas — <c>/facturacion/cajas</c> (CAJAS-PR5). Configuración
 * operativa del módulo: vive en Facturación, no en /admin (excepción
 * declarada al ADR-0034, 12-cajas.md §8).
 */
export const Route = createFileRoute('/_app/facturacion/cajas/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.FacturacionCajaAdministrar)) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: BandejaCajas,
  validateSearch: (input: Record<string, unknown>): CajasSearch =>
    CajasSearchSchema.parse(input),
});
