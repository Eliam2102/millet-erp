import { createFileRoute, redirect } from '@tanstack/react-router';
import { CierreMesPage } from '@/features/almacen/pages/CierreMesPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Ruta P12 — Cierre de mes (doc 07 §FE-F6-PR1).
 * Gate: <c>almacen.cierre-mes.ejecutar</c> (Jefe Almacén).
 */
export const Route = createFileRoute('/_app/almacen/cierre-mes')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.AlmacenCierreMesEjecutar)) {
      throw redirect({ to: '/almacen' });
    }
  },
  component: CierreMesPage,
});
