import { useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { PowerOff, X } from 'lucide-react';
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
import { useEmpresa, useDesactivarEmpresa } from '@/modules/administracion/api';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { EmpresaDatosForm } from '@/modules/administracion/components/EmpresaDatosForm';
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
  const empresaQuery = useEmpresa(id);
  const [confirmDesactivar, setConfirmDesactivar] = useState(false);

  const canDesactivar = useHasPermission(
    PermisosCanonicos.AdminEmpresasDesactivar,
  );
  const idempotencyKey = useFormIdempotencyKey();
  const desactivar = useDesactivarEmpresa();

  if (empresaQuery.isError) {
    const problem = esApiError(empresaQuery.error)
      ? empresaQuery.error.problem
      : undefined;
    return <ErrorState problem={problem} onRetry={() => empresaQuery.refetch()} />;
  }

  if (empresaQuery.isLoading || empresaQuery.data == null) {
    return (
      <div className="space-y-4 p-4">
        <TableSkeleton
          rows={4}
          columns={[{ width: 'w-48' }, { width: 'w-64' }, { width: 'w-32' }]}
        />
      </div>
    );
  }

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
                  : error.traceId
                    ? `Código: ${error.traceId}`
                    : undefined,
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
    <div className="flex flex-col">
      <header
        className="flex flex-wrap items-center gap-3 border-b bg-background px-4 py-2"
        data-print="hidden"
      >
        <div className="flex min-w-0 items-center gap-2">
          <span className="truncate font-mono text-sm font-semibold">
            {empresa.rfc}
          </span>
          {empresa.activa ? (
            <Badge variant="secondary">Activa</Badge>
          ) : (
            <Badge variant="outline" className="text-muted-foreground">
              Inactiva
            </Badge>
          )}
          <span className="hidden truncate text-sm text-muted-foreground md:inline">
            · {empresa.razonSocial}
          </span>
        </div>

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
      </header>

      <div className="px-4 pt-4 pb-6">
        <EmpresaDatosForm empresa={empresa} />
      </div>

      <AlertDialog
        open={confirmDesactivar}
        onOpenChange={setConfirmDesactivar}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar empresa</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar la empresa{' '}
              <span className="font-mono font-semibold">{empresa.rfc}</span>?
              Las sucursales activas deben desactivarse primero. La acción
              se puede revertir contactando al administrador.
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
