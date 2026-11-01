import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { BandejaCancelaciones } from '@/features/facturacion/pages/BandejaCancelaciones';

/** F6.5: bandeja del Contador General para autorizar cancelaciones de CFDI. */
export const Route = createFileRoute('/_app/facturacion/cancelaciones')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.FacturacionCancelacionesConsultar))
      throw redirect({ to: '/facturacion' });
  },
  component: BandejaCancelaciones,
});
