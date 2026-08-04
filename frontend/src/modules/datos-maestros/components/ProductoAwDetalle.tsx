import { useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { PowerOff, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
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
  useDesactivarProductoAw,
  useProductoAw,
} from '@/modules/datos-maestros/api';
import { EstatusCatalogo } from '@/modules/datos-maestros/api/types';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ProductoAwDatosForm } from '@/modules/datos-maestros/components/ProductoAwDatosForm';
import {
  EstatusCatalogoBadge,
  FiscalesIncompletosBadge,
  OrigenBadge,
} from '@/modules/datos-maestros/components/master-badges';

/**
 * Detalle de producto A+W (P3 del patrón cross-módulo, ADR-0048).
 * Análogo a <see cref="ClienteDetalle"/>: header NO sticky con
 * referencia + badges (estatus, origen, fiscales incompletos) + acción
 * Desactivar; sin tabs — directamente
 * <see cref="ProductoAwDatosForm"/>.
 */
export function ProductoAwDetalle() {
  const { id } = useParams({
    from: '/_app/admin/datos-maestros/productos-aw/$id',
  });
  const productoQuery = useProductoAw(id);
  const [confirmDesactivar, setConfirmDesactivar] = useState(false);

  const canGestionar = useHasPermission(
    PermisosCanonicos.DatosMaestrosProductosAwGestionar,
  );
  const desactivar = useDesactivarProductoAw();

  if (productoQuery.isError) {
    const problem = esApiError(productoQuery.error)
      ? productoQuery.error.problem
      : undefined;
    return (
      <ErrorState problem={problem} onRetry={() => productoQuery.refetch()} />
    );
  }

  if (productoQuery.isLoading || productoQuery.data == null) {
    return (
      <div className="space-y-4 p-4">
        <TableSkeleton
          rows={4}
          columns={[{ width: 'w-48' }, { width: 'w-64' }, { width: 'w-32' }]}
        />
      </div>
    );
  }

  const producto = productoQuery.data;
  const activo = producto.estatus === EstatusCatalogo.Activo;

  function handleConfirmarDesactivar() {
    desactivar.mutate(
      // Key fresca por acción: el detalle queda montado al navegar entre
      // productos; una key estable replicaría el 204 cacheado del 1º y NO
      // desactivaría el 2º (ADR-0020).
      { id: producto.id, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success(
            `Producto ${producto.referenciaExterna} desactivado`,
          );
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
            toast.error('Error al desactivar el producto A+W.');
          }
          setConfirmDesactivar(false);
        },
      },
    );
  }

  return (
    <div className="flex flex-col">
      {/* Header NO sticky: viaja con el scroll para no tapar la primera
          fila del form al anclar. Sin nav porque no hay tabs. */}
      <header
        className="flex flex-wrap items-center gap-3 border-b bg-background px-4 py-2"
        data-print="hidden"
      >
        <div className="flex min-w-0 flex-wrap items-center gap-2">
          <span className="truncate font-mono text-sm font-semibold">
            {producto.referenciaExterna}
          </span>
          <EstatusCatalogoBadge estatus={producto.estatus} />
          <OrigenBadge origen={producto.origen} />
          <FiscalesIncompletosBadge
            completos={producto.datosFiscalesCompletos}
          />
          <span className="hidden truncate text-sm text-muted-foreground md:inline">
            · {producto.descripcion}
          </span>
        </div>

        <div className="ml-auto flex items-center gap-1">
          {canGestionar && activo && (
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
            <Link to="/admin/datos-maestros/productos-aw" aria-label="Cerrar">
              <X className="h-4 w-4" />
            </Link>
          </Button>
        </div>
      </header>

      <div className="px-4 pt-4 pb-6">
        <ProductoAwDatosForm producto={producto} />
      </div>

      <AlertDialog
        open={confirmDesactivar}
        onOpenChange={setConfirmDesactivar}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar producto A+W</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar el producto{' '}
              <span className="font-mono font-semibold">
                {producto.referenciaExterna}
              </span>
              ? Los documentos históricos siguen funcionando con su id. Es
              un soft delete: el registro se conserva para la correlación
              con A+W.
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
