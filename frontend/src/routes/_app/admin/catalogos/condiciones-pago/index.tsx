import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { CondicionesPagoPage } from '@/modules/catalogos/components/CondicionesPagoPage';
import { NuevaCondicionesPagoProvider } from '@/modules/catalogos/components/SheetNuevaCondicionesPago';

/**
 * <c>/admin/catalogos/condiciones-pago</c> — bandeja P1 (UF-Admin-PR5.2).
 * Guard <c>catalogos.condiciones-pago.gestionar</c> O lectura general.
 */
export const Route = createFileRoute(
  '/_app/admin/catalogos/condiciones-pago/',
)({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.CompartidoCatalogosLeer) &&
      !permisos.includes(PermisosCanonicos.CatalogosCondicionesPagoGestionar)
    ) {
      throw redirect({ to: '/' });
    }
  },
  component: CondicionesPagoIndexRoute,
});

function CondicionesPagoIndexRoute() {
  return (
    <NuevaCondicionesPagoProvider>
      <CondicionesPagoPage />
    </NuevaCondicionesPagoProvider>
  );
}
