import { DialogBody } from '@/components/ui/dialog';
import { ConfirmacionContent as AlertDialogContent } from '@/modules/administracion/components/ConfirmacionContent';
import { PageHeader } from '@/components/erp/PageHeader';
import { useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { PowerOff, X } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { useEmpresa, useDesactivarEmpresa } from '@/modules/administracion/api';
import { esApiError, useFormIdempotencyKey, codigoSoporte } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { EmpresaDatosForm } from '@/modules/administracion/components/EmpresaDatosForm';
import { MiEmpresaContenido } from './MiEmpresaContenido';
import { toast } from 'sonner';
import type { EmpresaResponse } from '@/modules/administracion/api/types';

/**
 * Detalle de empresa — link "avanzado" (RFC, régimen fiscal, razón
 * social). Con el modelo multisucursal confirmado (ADR-0051, una sola
 * empresa) esta pantalla ya NO es el master-detail con tabs Sucursales
 * / Departamentos — esos ejes viven en <c>/admin/sucursales</c> y
 * <c>/admin/departamentos</c> como cards propias. No hay alta de
 * empresas nuevas desde la UI (única empresa: Millet); esta pantalla
 * solo visualiza/edita los datos de la que ya existe.
 *
 * <para>Lee <c>$id</c> del path con <c>useParams({ from:
 * '/_app/admin/empresas/$id' })</c>. 403/404 caen al
 * <c>ErrorState</c>.</para>
 */
export function EmpresaDetalle() {
  const { id } = useParams({ from: '/_app/admin/empresas/$id' });
  return <EmpresaDetallePorId id={id} />;
}

export function EmpresaDetallePorId({
  id,
  miEmpresa = false,
}: {
  id: string;
  miEmpresa?: boolean;
}) {
  const empresaQuery = useEmpresa(id);
  const [confirmDesactivar, setConfirmDesactivar] = useState(false);

  const canDesactivar = useHasPermission(PermisosCanonicos.AdminEmpresasDesactivar);
  const idempotencyKey = useFormIdempotencyKey();
  const desactivar = useDesactivarEmpresa();

  if (empresaQuery.isError) {
    const problem = esApiError(empresaQuery.error) ? empresaQuery.error.problem : undefined;
    return <ErrorState problem={problem} onRetry={() => empresaQuery.refetch()} />;
  }

  if (empresaQuery.isLoading || empresaQuery.data == null) {
    return (
      <div className="space-y-5">
        <TableSkeleton
          rows={4}
          columns={[{ width: 'w-48' }, { width: 'w-64' }, { width: 'w-32' }]}
        />
      </div>
    );
  }

  if (miEmpresa) return <MiEmpresaContenido detalle={empresaQuery.data} />;

  const { empresa } = empresaQuery.data;

  return (
    <EmpresaDetalleContenido
      empresa={empresa}
      confirmDesactivar={confirmDesactivar}
      setConfirmDesactivar={setConfirmDesactivar}
      canDesactivar={canDesactivar}
      desactivar={desactivar}
      idempotencyKey={idempotencyKey}
    />
  );
}

interface EmpresaDetalleContenidoProps {
  empresa: EmpresaResponse;
  confirmDesactivar: boolean;
  setConfirmDesactivar: (value: boolean) => void;
  canDesactivar: boolean;
  desactivar: ReturnType<typeof useDesactivarEmpresa>;
  idempotencyKey: string;
}

function EmpresaDetalleContenido({
  empresa,
  confirmDesactivar,
  setConfirmDesactivar,
  canDesactivar,
  desactivar,
  idempotencyKey,
}: EmpresaDetalleContenidoProps) {
  function handleConfirmarDesactivar() {
    desactivar.mutate(
      { id: empresa.id, idempotencyKey },
      {
        onSuccess: () => {
          toast.success(`Empresa ${empresa.rfc} desactivada`);
          setConfirmDesactivar(false);
        },
        onError: (error) => {
          if (esApiError(error)) {
            // El handler valida que no haya sucursales activas; si la hay,
            // backend devuelve 422 SUCURSALES_ACTIVAS_PENDIENTES.
            toast.error(error.problem.title, {
              description:
                error.code === 'SUCURSALES_ACTIVAS_PENDIENTES'
                  ? 'Desactiva primero las sucursales activas.'
                  : codigoSoporte(error),
            });
          } else {
            toast.error('Error al desactivar la empresa.');
          }
          setConfirmDesactivar(false);
        },
      },
    );
  }

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        mono
        titulo={empresa.rfc}
        descripcion={empresa.razonSocial}
        estado={
          <>
            {empresa.activa ? (
              <Badge variant="neutral">Activa</Badge>
            ) : (
              <Badge variant="neutral">Inactiva</Badge>
            )}
          </>
        }
        acciones={
          <div className="ml-auto flex items-center gap-1">
            {canDesactivar && empresa.activa && (
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
              <Link to="/admin/sucursales" aria-label="Cerrar">
                <X className="h-4 w-4" />
              </Link>
            </Button>
          </div>
        }
      />
      <dl className="grid gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat sm:grid-cols-4">
        <div>
          <dt className="text-xs text-ink-muted">RFC</dt>
          <dd className="text-sm font-medium font-mono">{empresa.rfc}</dd>
        </div>
        <div>
          <dt className="text-xs text-ink-muted">Razón social</dt>
          <dd className="text-sm font-medium ">{empresa.razonSocial}</dd>
        </div>
      </dl>

      <div className="px-4 pt-4 pb-6">
        <EmpresaDatosForm key={empresa.id} empresa={empresa} />
      </div>

      <AlertDialog open={confirmDesactivar} onOpenChange={setConfirmDesactivar}>
        <AlertDialogContent>
          <AlertDialogHeader className="px-6 pt-5 pb-3">
            <AlertDialogTitle>Desactivar empresa</AlertDialogTitle>
          </AlertDialogHeader>
          <DialogBody>
            <AlertDialogDescription>
              ¿Confirmas desactivar la empresa{' '}
              <span className="font-mono font-semibold">{empresa.rfc}</span>? Las sucursales activas
              deben desactivarse primero. La acción se puede revertir contactando al administrador.
            </AlertDialogDescription>
          </DialogBody>
          <AlertDialogFooter className="border-t border-line-divider bg-surface-subtle px-6 py-4">
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
