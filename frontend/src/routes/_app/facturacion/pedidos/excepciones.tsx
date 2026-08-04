import { createFileRoute, redirect } from '@tanstack/react-router';
import { BandejaExcepciones } from '@/features/facturacion/pages/BandejaExcepciones';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Bandeja de excepciones de ingesta —
 * <c>/facturacion/pedidos/excepciones</c> (FE-F3).
 */
export const Route = createFileRoute('/_app/facturacion/pedidos/excepciones')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(
        PermisosCanonicos.FacturacionPedidosExcepcionesResolver,
      )
    ) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: BandejaExcepciones,
});
