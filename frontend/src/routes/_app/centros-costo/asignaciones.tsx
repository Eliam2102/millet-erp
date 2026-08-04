import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { AsignacionCentrosCostoPage } from '@/features/centros-costo/pages/AsignacionCentrosCostoPage';

/**
 * <c>/centros-costo/asignaciones</c> — Módulo 2 del 05 §2: árbol de
 * asignación de 5 niveles con tri-estado por usuario (FE-PR3). Guard por
 * `asignaciones.administrar` (nace en FE-PR1).
 */
export const Route = createFileRoute('/_app/centros-costo/asignaciones')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.CentrosCostoAsignacionesAdministrar)
    ) {
      throw redirect({ to: '/' });
    }
  },
  component: AsignacionCentrosCostoPage,
});
