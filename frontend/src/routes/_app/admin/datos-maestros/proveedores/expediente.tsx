import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ExpedienteTiposPage } from '@/modules/datos-maestros/components/ExpedienteTiposPage';

/**
 * <c>/admin/datos-maestros/proveedores/expediente</c> — matriz de documentos
 * del expediente de proveedor. Requiere <c>proveedores.gestionar</c> (lectura);
 * editar exige además <c>expediente-configurar</c> (lo valida la página y la API).
 */
export const Route = createFileRoute('/_app/admin/datos-maestros/proveedores/expediente')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (!permisos.includes(PermisosCanonicos.DatosMaestrosProveedoresGestionar)) {
      throw redirect({ to: '/' });
    }
  },
  component: ExpedienteTiposPage,
});
