import { createFileRoute, redirect } from '@tanstack/react-router';
import { EmitirFacturaPage } from '@/features/facturacion/pages/EmitirFacturaPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Factura manual — <c>/facturacion/facturas/nueva</c> (FAC-UX-PR2).
 * Form de emisión con pestañas; reemplaza al Sheet del provider. El
 * segmento estático gana sobre <c>$id</c>, sin conflicto de rutas.
 */
export const Route = createFileRoute('/_app/facturacion/facturas/nueva')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.FacturacionFacturasEmitir)) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: EmitirFacturaPage,
});
