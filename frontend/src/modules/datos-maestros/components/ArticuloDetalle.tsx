import { useState } from 'react';
import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { ArrowRight, MapPin, PowerOff, X } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useAsignacionesList } from '@/features/almacen/api/useAsignaciones';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { ErrorState, TableSkeleton } from '@/components/erp';
import {
  useArticulo,
  useDesactivarArticulo,
} from '@/modules/datos-maestros/api';
import { EstatusCatalogo } from '@/modules/datos-maestros/api/types';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ArticuloDatosForm } from '@/modules/datos-maestros/components/ArticuloDatosForm';

/**
 * Detalle de artículo (P3 del patrón cross-módulo). Análogo a
 * <see cref="ProveedorDetalle"/>: header NO sticky con clave + nombre +
 * badge + acción Desactivar; sin tabs porque solo hay una sección.
 */
export function ArticuloDetalle() {
  const { id } = useParams({
    from: '/_app/admin/datos-maestros/articulos/$id',
  });
  const articuloQuery = useArticulo(id);
  const [confirmDesactivar, setConfirmDesactivar] = useState(false);

  const canDesactivar = useHasPermission(
    PermisosCanonicos.CompartidoCatalogosAdministrar,
  );
  // Atajo a "Ubicación de artículos". Gateado por el permiso que exige la ruta
  // destino y su GET del conteo — sin él, ni el botón ni la query existen.
  const canVerUbicaciones = useHasPermission(
    PermisosCanonicos.AlmacenAsignacionesRead,
  );
  const desactivar = useDesactivarArticulo();

  if (articuloQuery.isError) {
    const problem = esApiError(articuloQuery.error)
      ? articuloQuery.error.problem
      : undefined;
    return (
      <ErrorState problem={problem} onRetry={() => articuloQuery.refetch()} />
    );
  }

  if (articuloQuery.isLoading || articuloQuery.data == null) {
    return (
      <div className="space-y-4 p-4">
        <TableSkeleton
          rows={4}
          columns={[{ width: 'w-48' }, { width: 'w-64' }, { width: 'w-32' }]}
        />
      </div>
    );
  }

  const articulo = articuloQuery.data;
  const activo = articulo.estatus === EstatusCatalogo.Activo;

  function handleConfirmarDesactivar() {
    desactivar.mutate(
      // Key fresca por acción: el detalle queda montado al navegar entre
      // artículos; una key estable replicaría el 204 cacheado del 1º y NO
      // desactivaría el 2º (ADR-0020).
      { id: articulo.id, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success(`Artículo ${articulo.clave} desactivado`);
          setConfirmDesactivar(false);
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: error.traceId
                ? `Código: ${error.traceId}`
                : undefined,
            });
          } else {
            toast.error('Error al desactivar el artículo.');
          }
          setConfirmDesactivar(false);
        },
      },
    );
  }

  return (
    <div className="flex flex-col">
      <header
        className="flex flex-wrap items-center gap-3 border-b bg-background px-4 py-2"
        data-print="hidden"
      >
        <div className="flex min-w-0 items-center gap-2">
          <span className="truncate font-mono text-sm font-semibold">
            {articulo.clave}
          </span>
          <EstatusBadge estatus={articulo.estatus} />
          <span className="hidden truncate text-sm text-muted-foreground md:inline">
            · {articulo.nombre}
          </span>
        </div>

        <div className="ml-auto flex items-center gap-1">
          {canVerUbicaciones && (
            <VerUbicacionesBoton
              articuloId={articulo.id}
              etiqueta={`${articulo.clave} · ${articulo.nombre}`}
            />
          )}
          {canDesactivar && activo && (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => setConfirmDesactivar(true)}
            >
              <PowerOff className="mr-1.5 h-4 w-4" />
              Desactivar
            </Button>
          )}
          <Button asChild variant="ghost" size="icon">
            <Link to="/admin/datos-maestros/articulos" aria-label="Cerrar">
              <X className="h-4 w-4" />
            </Link>
          </Button>
        </div>
      </header>

      <div className="px-4 pt-4 pb-6">
        <ArticuloDatosForm articulo={articulo} />
      </div>

      <AlertDialog
        open={confirmDesactivar}
        onOpenChange={setConfirmDesactivar}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar artículo</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar el artículo{' '}
              <span className="font-mono font-semibold">{articulo.clave}</span>?
              Las RQs históricas siguen funcionando con su id; las nuevas
              RQs se bloquean por la validación cross-table.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={desactivar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={handleConfirmarDesactivar}
              disabled={desactivar.isPending}
            >
              {desactivar.isPending ? 'Desactivando…' : 'Desactivar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

/**
 * Botón "Ver ubicaciones (N)" → navega a la pantalla "Ubicación de artículos"
 * pre-filtrada por este artículo, con el sentinel <c>desde=articulo</c> y su
 * etiqueta (para el breadcrumb de retorno). Vive en su propio componente para
 * que el hook de conteo solo corra cuando el padre lo monta (con permiso): así
 * la query no dispara para quien no puede ver ubicaciones. El conteo usa
 * <c>limit=1</c> y lee el <c>total</c> server-side (no trae la lista).
 */
function VerUbicacionesBoton({
  articuloId,
  etiqueta,
}: {
  articuloId: string;
  etiqueta: string;
}) {
  const navigate = useNavigate();
  const conteo = useAsignacionesList({
    articuloId,
    estatus: EstatusCatalogo.Activo,
    limit: 1,
  });
  const total = conteo.data?.total;

  return (
    <Button
      type="button"
      variant="outline"
      size="sm"
      onClick={() =>
        navigate({
          to: '/almacen/asignaciones',
          search: { articuloId, desde: 'articulo', articuloEtiqueta: etiqueta },
        })
      }
    >
      <MapPin className="mr-1.5 h-4 w-4" />
      Ver ubicaciones{total != null ? ` (${total})` : ''}
      <ArrowRight className="ml-1.5 h-4 w-4" />
    </Button>
  );
}

function EstatusBadge({ estatus }: { estatus: number }) {
  if (estatus === EstatusCatalogo.Activo) {
    return <Badge variant="secondary">Activo</Badge>;
  }
  if (estatus === EstatusCatalogo.EnRevision) {
    return <Badge variant="outline">En revisión</Badge>;
  }
  return (
    <Badge variant="outline" className="text-muted-foreground">
      Inactivo
    </Badge>
  );
}
