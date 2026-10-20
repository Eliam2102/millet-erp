import { PageHeader } from '@/components/erp/PageHeader';
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
import { useDesactivarProductoAw, useProductoAw } from '@/modules/datos-maestros/api';
import { EstatusCatalogo } from '@/modules/datos-maestros/api/types';
import { esApiError, codigoSoporte } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  ProductoAwBajaAviso,
  ProductoAwComposicion,
  ProductoAwOrigenSection,
  ProductoAwVariantesTable,
} from '@/modules/datos-maestros/components/ProductoAwOrigenSection';
import { F1AProductoFiscal } from './F1AFiscalPanel';
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

  const canGestionar = useHasPermission(PermisosCanonicos.DatosMaestrosProductosAwGestionar);
  const desactivar = useDesactivarProductoAw();

  if (productoQuery.isError) {
    const problem = esApiError(productoQuery.error) ? productoQuery.error.problem : undefined;
    return <ErrorState problem={problem} onRetry={() => productoQuery.refetch()} />;
  }

  if (productoQuery.isLoading || productoQuery.data == null) {
    return (
      <div className="space-y-5">
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
          toast.success(`Producto ${producto.referenciaExterna} desactivado`);
          setConfirmDesactivar(false);
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: codigoSoporte(error),
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
    <div className="flex flex-col gap-5">
      {/* Header NO sticky: viaja con el scroll para no tapar la primera
          fila del form al anclar. Sin nav porque no hay tabs. */}
      <PageHeader
        mono
        titulo={<>{producto.referenciaExterna}</>}
        descripcion={<>{producto.descripcion}</>}
        estado={
          <>
            <EstatusCatalogoBadge estatus={producto.estatus} />
            <OrigenBadge origen={producto.origen} />
            <FiscalesIncompletosBadge completos={producto.datosFiscalesCompletos} />
          </>
        }
        acciones={
          <>
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
          </>
        }
      />
      <dl className="grid grid-cols-2 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat">
        <div>
          <dt className="text-xs text-ink-muted">Unidad de medida</dt>
          <dd className="font-mono text-sm">{producto.unidadMedida ?? '—'}</dd>
        </div>
        <div>
          <dt className="text-xs text-ink-muted">Clave SAT</dt>
          <dd className="font-mono text-sm">{producto.claveProdServSat ?? '—'}</dd>
        </div>
      </dl>

      <div className="space-y-5">
        <ProductoAwBajaAviso producto={producto} />
        {canGestionar && <F1AProductoFiscal producto={producto} />}
        <ProductoAwDatosForm producto={producto} />
        <ProductoAwVariantesTable
          variantes={producto.variantes ?? []}
          unidadMedida={producto.unidadMedida}
        />
        <ProductoAwComposicion componentes={producto.componentes ?? []} />
        <ProductoAwOrigenSection producto={producto} />
      </div>

      <AlertDialog open={confirmDesactivar} onOpenChange={setConfirmDesactivar}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar producto A+W</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar el producto{' '}
              <span className="font-mono font-semibold">{producto.referenciaExterna}</span>? Los
              documentos históricos siguen funcionando con su id. Es un soft delete: el registro se
              conserva para la correlación con A+W.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={desactivar.isPending}>Cancelar</AlertDialogCancel>
            <AlertDialogAction onClick={handleConfirmarDesactivar} disabled={desactivar.isPending}>
              {desactivar.isPending ? 'Desactivando…' : 'Desactivar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
