import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { MovimientosPruebaPage } from '@/features/contabilidad/pages/MovimientosPruebaPage';

/** Guard de UX: sin ContabilidadMovimientosValidar se redirige; la autorización real es del API (403 y alcance por sucursal). */
export const Route = createFileRoute('/_app/contabilidad/movimientos-prueba')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadMovimientosValidar)) throw redirect({ to: '/' });
  },
  component: MovimientosPruebaPage,
});
