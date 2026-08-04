import { useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { PowerOff, X } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
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
  useDesactivarProveedor,
  useProveedor,
} from '@/modules/datos-maestros/api';
import { EstatusCatalogo } from '@/modules/datos-maestros/api/types';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ProveedorDatosForm } from '@/modules/datos-maestros/components/ProveedorDatosForm';

/**
 * Detalle de proveedor (P3 del patrón cross-módulo). Header en flow
 * normal (NO sticky) con clave + razón social + badge de estatus +
 * acción Desactivar (si activo) + botón cerrar. Sin tabs porque solo
 * hay una sección — directamente se renderiza
 * <see cref="ProveedorDatosForm"/>.
 *
 * <para>Lee <c>$id</c> con la forma estricta
 * <c>useParams({ from: '/_app/admin/datos-maestros/proveedores/$id' })</c>
 * para typing exacto y evitar que el id se cuele <c>undefined</c> en
 * runtime. 403/404 caen al <c>ErrorState</c>.</para>
 *
 * <para><b>Sticky pattern</b>: replica el de <c>EmpresaDetalle</c>
 * con la diferencia de que aquí NO hay <c>nav</c> sticky-top-14 (no
 * hay tabs). El header viaja con el scroll para no obstruir la primera
 * fila del form al anclar.</para>
 */
export function ProveedorDetalle() {
  const { id } = useParams({
    from: '/_app/admin/datos-maestros/proveedores/$id',
  });
  const proveedorQuery = useProveedor(id);
  const [confirmDesactivar, setConfirmDesactivar] = useState(false);

  const canDesactivar = useHasPermission(
    PermisosCanonicos.CompartidoCatalogosAdministrar,
  );
  const desactivar = useDesactivarProveedor();

  if (proveedorQuery.isError) {
    const problem = esApiError(proveedorQuery.error)
      ? proveedorQuery.error.problem
      : undefined;
    return (
      <ErrorState problem={problem} onRetry={() => proveedorQuery.refetch()} />
    );
  }

  if (proveedorQuery.isLoading || proveedorQuery.data == null) {
    return (
      <div className="space-y-4 p-4">
        <TableSkeleton
          rows={4}
          columns={[{ width: 'w-48' }, { width: 'w-64' }, { width: 'w-32' }]}
        />
      </div>
    );
  }

  const proveedor = proveedorQuery.data;
  const activo = proveedor.estatus === EstatusCatalogo.Activo;

  function handleConfirmarDesactivar() {
    desactivar.mutate(
      // Key fresca por acción: el detalle queda montado al navegar entre
      // proveedores; una key estable replicaría el 204 cacheado del 1º y NO
      // desactivaría el 2º (ADR-0020).
      { id: proveedor.id, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success(`Proveedor ${proveedor.clave} desactivado`);
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
            toast.error('Error al desactivar el proveedor.');
          }
          setConfirmDesactivar(false);
        },
      },
    );
  }

  return (
    <div className="flex flex-col">
      {/* Header NO sticky: viaja con el scroll para no tapar la primera
          fila del form al anclar. Sin nav porque no hay tabs (solo una
          sección). */}
      <header
        className="flex flex-wrap items-center gap-3 border-b bg-background px-4 py-2"
        data-print="hidden"
      >
        <div className="flex min-w-0 items-center gap-2">
          <span className="truncate font-mono text-sm font-semibold">
            {proveedor.clave}
          </span>
          <EstatusBadge estatus={proveedor.estatus} />
          <span className="hidden truncate text-sm text-muted-foreground md:inline">
            · {proveedor.razonSocial}
          </span>
        </div>

        <div className="ml-auto flex items-center gap-1">
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
            <Link to="/admin/datos-maestros/proveedores" aria-label="Cerrar">
              <X className="h-4 w-4" />
            </Link>
          </Button>
        </div>
      </header>

      <div className="px-4 pt-4 pb-6">
        <ProveedorDatosForm proveedor={proveedor} />
      </div>

      <AlertDialog
        open={confirmDesactivar}
        onOpenChange={setConfirmDesactivar}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar proveedor</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar al proveedor{' '}
              <span className="font-mono font-semibold">{proveedor.clave}</span>?
              Las RQs históricas siguen funcionando con su id; las nuevas
              RQs se bloquean por la validación cross-table. Para reactivar,
              edita el campo Estatus desde el detalle (cuando llegue ese
              control).
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
