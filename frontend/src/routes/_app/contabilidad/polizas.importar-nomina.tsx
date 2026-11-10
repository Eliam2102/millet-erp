import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ImportarNominaPage } from '@/features/contabilidad/pages/ImportarNominaPage';

/** Guard de UX: sin ContabilidadPolizasCapturar se redirige; la autorización real es del API (403). */
export const Route = createFileRoute('/_app/contabilidad/polizas/importar-nomina')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadPolizasCapturar)) throw redirect({ to: '/' });
  },
  component: ImportarNominaPage,
});
