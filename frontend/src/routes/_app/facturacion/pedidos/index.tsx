import { createFileRoute, redirect } from '@tanstack/react-router';
import { BandejaPedidos } from '@/features/facturacion/pages/BandejaPedidos';
import {
  PedidosSearchSchema,
  type PedidosSearch,
} from '@/features/facturacion/lib/pedidos-search-schema';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Bandeja de pedidos facturables — <c>/facturacion/pedidos</c> (FE-F1-PR1).
 * Visible para quien emite facturas, captura pedidos manuales o importa
 * de A+W.
 */
export const Route = createFileRoute('/_app/facturacion/pedidos/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    const permitido = [
      PermisosCanonicos.FacturacionFacturasEmitir,
      PermisosCanonicos.FacturacionPedidosCapturar,
      PermisosCanonicos.FacturacionPedidosImportar,
    ].some((p) => permisos.includes(p));
    if (!permitido) {
      throw redirect({ to: '/facturacion' });
    }
  },
  component: BandejaPedidos,
  validateSearch: (input: Record<string, unknown>): PedidosSearch =>
    PedidosSearchSchema.parse(input),
});
