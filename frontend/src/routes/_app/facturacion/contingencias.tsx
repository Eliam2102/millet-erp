import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { BandejaContingencias } from '@/features/facturacion/pages/BandejaContingencias';

/** F6.8: bitácora de contingencias de timbrado y su regularización. */
export const Route = createFileRoute('/_app/facturacion/contingencias')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.FacturacionFacturasLeer))
      throw redirect({ to: '/facturacion' });
  },
  component: BandejaContingencias,
});
