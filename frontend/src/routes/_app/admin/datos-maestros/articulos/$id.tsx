import { createFileRoute, redirect, useParams } from '@tanstack/react-router';
import { toast } from 'sonner';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ArticulosLayout } from '@/modules/datos-maestros/components/ArticulosLayout';
import { ArticuloDetalle } from '@/modules/datos-maestros/components/ArticuloDetalle';
import { NuevoArticuloProvider } from '@/modules/datos-maestros/components/SheetNuevoArticulo';

/**
 * <c>/admin/datos-maestros/articulos/$id</c> — detalle dentro del
 * layout master-detail. <c>key={id}</c> fuerza re-mount al navegar
 * entre artículos para evitar estado stale.
 */
export const Route = createFileRoute(
  '/_app/admin/datos-maestros/articulos/$id',
)({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    if (
      !permisos.includes(PermisosCanonicos.DatosMaestrosArticulosGestionar)
    ) {
      // F-RQOC-03: avisar en lugar de regresar a Inicio en silencio.
      toast.info('No tienes permiso para abrir Artículos.', { id: 'sin-acceso-articulos' });
      throw redirect({ to: '/' });
    }
  },
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = useParams({
    from: '/_app/admin/datos-maestros/articulos/$id',
  });
  return (
    <NuevoArticuloProvider>
      <ArticulosLayout idActivo={id} detalle={<ArticuloDetalle key={id} />} />
    </NuevoArticuloProvider>
  );
}
