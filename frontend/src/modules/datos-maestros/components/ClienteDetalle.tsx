import { PageHeader } from '@/components/erp/PageHeader';
import { CambioEstatusMaestroDialog } from './CambioEstatusMaestroDialog';
import { useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { Power, PowerOff, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { ErrorState, TableSkeleton } from '@/components/erp';
import {
  useCliente,
  useDesactivarCliente,
  useReactivarCliente,
} from '@/modules/datos-maestros/api';
import { EstatusCatalogo } from '@/modules/datos-maestros/api/types';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ClienteOrigenAwSection } from '@/modules/datos-maestros/components/ClienteOrigenAwSection';
import { ClienteSaldoGrupoSection } from '@/modules/datos-maestros/components/ClienteSaldoGrupoSection';
import { ClienteDatosForm } from '@/modules/datos-maestros/components/ClienteDatosForm';
import { QueFaltaPanel, type ItemFalta } from '@/modules/datos-maestros/components/QueFaltaPanel';
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
  const [dialogoEstatus, setDialogoEstatus] = useState<'desactivar' | 'reactivar' | null>(null);

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
  // Misma definición que el filtro «Fiscales incompletos»: RFC, régimen y CP.
  const faltantesFiscales = ([
    !cliente.rfc ? { etiqueta: 'RFC', campo: 'rfc' } : null,
    !cliente.regimenFiscal ? { etiqueta: 'Régimen fiscal', campo: 'regimenFiscal' } : null,
    !cliente.codigoPostalFiscal ? { etiqueta: 'Código postal fiscal', campo: 'codigoPostalFiscal' } : null,
  ] as (ItemFalta | null)[]).filter((f): f is ItemFalta => f !== null);

  function handleConfirmarEstatus(motivo: string) {
    const mutacion = dialogoEstatus === 'desactivar' ? desactivar : reactivar;
    mutacion.mutate(
      // Key fresca por acción: el detalle queda montado al navegar entre
      // clientes; una key estable replicaría el 204 cacheado del 1º (ADR-0020).
      { id: cliente.id, motivo, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success(
            `Cliente ${cliente.clave} ${dialogoEstatus === 'desactivar' ? 'desactivado' : 'reactivado'}`,
          );
          setDialogoEstatus(null);
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
        descripcion={
          <>
            {cliente.razonSocial}
            <span className="mt-0.5 block font-mono text-xs">
              RFC {cliente.rfc ?? 'sin RFC'} · {cliente.monedaDefault ?? '—'}
            </span>
          </>
        }
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
                onClick={() => {
                  desactivar.reset();
                  setDialogoEstatus('desactivar');
                }}
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
                onClick={() => {
                  reactivar.reset();
                  setDialogoEstatus('reactivar');
                }}
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
      <QueFaltaPanel items={faltantesFiscales} />

      <div className="space-y-5">
        <ClienteDatosForm cliente={cliente} />
        <ClienteOrigenAwSection cliente={cliente} />
        <ClienteSaldoGrupoSection clienteId={cliente.id} />
      </div>

      <CambioEstatusMaestroDialog
        modo={dialogoEstatus}
        etiqueta={`cliente ${cliente.clave}`}
        pending={desactivar.isPending || reactivar.isPending}
        error={dialogoEstatus === 'desactivar' ? desactivar.error : reactivar.error}
        onConfirm={handleConfirmarEstatus}
        onCancel={() => setDialogoEstatus(null)}
      />
    </div>
  );
}
