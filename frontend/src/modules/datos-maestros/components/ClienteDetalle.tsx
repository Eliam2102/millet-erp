import { PageHeader } from '@/components/erp/PageHeader';
import { useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { Power, PowerOff, X } from 'lucide-react';
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
  useCliente,
  useDesactivarCliente,
  useReactivarCliente,
} from '@/modules/datos-maestros/api';
import { EstatusCatalogo } from '@/modules/datos-maestros/api/types';
import { esApiError, codigoSoporte } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ClienteOrigenAwSection } from '@/modules/datos-maestros/components/ClienteOrigenAwSection';
import { ClienteDatosForm } from '@/modules/datos-maestros/components/ClienteDatosForm';
import {
  EstatusCatalogoBadge,
  FiscalesIncompletosBadge,
  OrigenBadge,
} from '@/modules/datos-maestros/components/master-badges';

/**
 * Detalle de cliente (P3 del patrón cross-módulo, ADR-0048). Análogo a
 * <see cref="ProveedorDetalle"/>: header NO sticky con clave + badges
 * (estatus, origen A+W/Manual, fiscales incompletos) + acción
 * Desactivar; sin tabs porque solo hay una sección — directamente
 * <see cref="ClienteDatosForm"/>.
 */
export function ClienteDetalle() {
  const { id } = useParams({
    from: '/_app/admin/datos-maestros/clientes/$id',
  });
  const clienteQuery = useCliente(id);
  const [confirmDesactivar, setConfirmDesactivar] = useState(false);

  const canGestionar = useHasPermission(PermisosCanonicos.DatosMaestrosClientesGestionar);
  const desactivar = useDesactivarCliente();
  const reactivar = useReactivarCliente();

  if (clienteQuery.isError) {
    const problem = esApiError(clienteQuery.error) ? clienteQuery.error.problem : undefined;
    return <ErrorState problem={problem} onRetry={() => clienteQuery.refetch()} />;
  }

  if (clienteQuery.isLoading || clienteQuery.data == null) {
    return (
      <div className="space-y-5">
        <TableSkeleton
          rows={4}
          columns={[{ width: 'w-48' }, { width: 'w-64' }, { width: 'w-32' }]}
        />
      </div>
    );
  }

  const cliente = clienteQuery.data;
  const activo = cliente.estatus === EstatusCatalogo.Activo;

  function handleConfirmarDesactivar() {
    desactivar.mutate(
      // Key fresca por acción: el detalle queda montado al navegar entre
      // clientes; una key estable replicaría el 204 cacheado del 1º y NO
      // desactivaría el 2º (ADR-0020).
      { id: cliente.id, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success(`Cliente ${cliente.clave} desactivado`);
          setConfirmDesactivar(false);
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: codigoSoporte(error),
            });
          } else {
            toast.error('Error al desactivar el cliente.');
          }
          setConfirmDesactivar(false);
        },
      },
    );
  }

  function handleReactivar() {
    reactivar.mutate(
      // Key fresca por acción (ADR-0020); misma razón que en desactivar.
      { id: cliente.id, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success(`Cliente ${cliente.clave} reactivado`);
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: codigoSoporte(error),
            });
          } else {
            toast.error('Error al reactivar el cliente.');
          }
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
        titulo={<>{cliente.clave}</>}
        descripcion={<>{cliente.razonSocial}</>}
        estado={
          <>
            <EstatusCatalogoBadge estatus={cliente.estatus} />
            <OrigenBadge origen={cliente.origen} />
            <FiscalesIncompletosBadge completos={cliente.datosFiscalesCompletos} />
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
            {canGestionar && !activo && (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                disabled={reactivar.isPending}
                onClick={handleReactivar}
              >
                <Power className="mr-1.5 h-4 w-4" />
                Reactivar
              </Button>
            )}
            <Button asChild variant="ghost" size="icon">
              <Link to="/admin/datos-maestros/clientes" aria-label="Cerrar">
                <X className="h-4 w-4" />
              </Link>
            </Button>
          </>
        }
      />
      <dl className="grid grid-cols-2 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat">
        <div>
          <dt className="text-xs text-ink-muted">RFC</dt>
          <dd className="font-mono text-sm">{cliente.rfc ?? '—'}</dd>
        </div>
        <div>
          <dt className="text-xs text-ink-muted">Moneda</dt>
          <dd className="font-mono text-sm">{cliente.monedaDefault ?? '—'}</dd>
        </div>
      </dl>

      <div className="space-y-5">
        <ClienteDatosForm cliente={cliente} />
        <ClienteOrigenAwSection cliente={cliente} />
      </div>

      <AlertDialog open={confirmDesactivar} onOpenChange={setConfirmDesactivar}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar cliente</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar al cliente{' '}
              <span className="font-mono font-semibold">{cliente.clave}</span>? Los documentos
              históricos se conservan y el registro mantiene su relación con A+W.
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
