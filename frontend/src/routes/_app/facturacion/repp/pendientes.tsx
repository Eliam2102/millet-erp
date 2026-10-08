import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { PendientesRepp } from '@/features/facturacion/pages/PendientesRepp';

export const Route = createFileRoute('/_app/facturacion/repp/pendientes')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.FacturacionFacturasLeer))
      throw redirect({ to: '/facturacion' });
  },
  component: PendientesRepp,
});
