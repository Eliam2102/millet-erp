import { useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import {
  CheckCircle2,
  Clock,
  PowerOff,
  X,
  XCircle,
} from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/textarea';
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
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { ErrorState, TableSkeleton } from '@/components/erp';
import {
  useDesactivarProveedor,
  useProveedor,
  useRechazarProveedor,
  useValidarProveedor,
} from '@/modules/datos-maestros/api';
import { EstatusCatalogo } from '@/modules/datos-maestros/api/types';
import { useExpediente } from '@/components/erp/adjuntos/api';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ProveedorDatosForm } from '@/modules/datos-maestros/components/ProveedorDatosForm';
import { ProveedorBancariosSection } from '@/modules/datos-maestros/components/ProveedorBancariosSection';
import { ProveedorExpedienteSection } from '@/modules/datos-maestros/components/ProveedorExpedienteSection';

/**
 * Detalle de proveedor (P3 del patrón cross-módulo). Header en flow
 * normal (NO sticky) con clave + razón social + badge de estatus +
 * acciones contextuales (Validar, Rechazar, Desactivar) + botón cerrar.
 * Sin tabs porque son pocas secciones — se renderizan directamente
 * <see cref="ProveedorDatosForm"/> y, si el usuario tiene el permiso
 * <c>bancarios-ver</c> (F1-ADM-05), <see cref="ProveedorBancariosSection"/> y, con <c>adjuntos-ver</c>
 * (F1-ADM-11 G1.2), <see cref="ProveedorExpedienteSection"/>.
 *
 * <para>G1.1 (F1-ADM-05): los proveedores nacen en <c>EnRevision</c>.
 * CxP con permiso <c>datos_maestros.proveedores.validar</c> puede
 * validar (si el expediente documental está completo) o rechazar con
 * motivo (5–500 caracteres).</para>
 */
export function ProveedorDetalle() {
  const { id } = useParams({
    from: '/_app/admin/datos-maestros/proveedores/$id',
  });
  const proveedorQuery = useProveedor(id);
  const [confirmDesactivar, setConfirmDesactivar] = useState(false);
  const [confirmValidar, setConfirmValidar] = useState(false);
  const [modalRechazar, setModalRechazar] = useState(false);
  const [motivoRechazo, setMotivoRechazo] = useState('');

  const canDesactivar = useHasPermission(
    PermisosCanonicos.CompartidoCatalogosAdministrar,
  );
  const canValidar = useHasPermission(
    PermisosCanonicos.DatosMaestrosProveedoresValidar,
  );
  const canVerAdjuntos = useHasPermission(
    PermisosCanonicos.DatosMaestrosProveedoresAdjuntosVer,
  );

  const desactivar = useDesactivarProveedor();
  const validar = useValidarProveedor();
  const rechazar = useRechazarProveedor();

  const expediente = useExpediente(
    `/api/v1/datos-maestros/proveedores/${id}`,
    { enabled: canVerAdjuntos || canValidar },
  );

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
  const enRevision = proveedor.estatus === EstatusCatalogo.EnRevision;

  const expedienteCompleto = expediente.data?.completo === true;
  const faltantes = expediente.data?.faltantes ?? [];
  const faltantesCount = faltantes.length;

  const motivoTrim = motivoRechazo.trim();
  const motivoValido = motivoTrim.length >= 5 && motivoTrim.length <= 500;

  function handleConfirmarDesactivar() {
    desactivar.mutate(
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

  function handleConfirmarValidar() {
    validar.mutate(
      { id: proveedor.id, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success(`Proveedor ${proveedor.clave} validado y activado exitosamente`);
          setConfirmValidar(false);
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: error.problem.detail ?? error.traceId,
            });
          } else {
            toast.error('Error al validar el proveedor.');
          }
          setConfirmValidar(false);
        },
      },
    );
  }

  function handleConfirmarRechazar() {
    if (!motivoValido) return;
    rechazar.mutate(
      {
        id: proveedor.id,
        motivo: motivoTrim,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success(`Proveedor ${proveedor.clave} rechazado (estatus Inactivo)`);
          setModalRechazar(false);
          setMotivoRechazo('');
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: error.problem.detail ?? error.traceId,
            });
          } else {
            toast.error('Error al rechazar el proveedor.');
          }
        },
      },
    );
  }

  return (
    <div className="flex flex-col">
      {/* Header NO sticky: viaja con el scroll para no tapar la primera
          fila del form al anclar. */}
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

        <div className="ml-auto flex items-center gap-2">
          {/* Acciones de CxP para proveedores En Revisión (G1.1) */}
          {enRevision && canValidar && (
            <>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="border-destructive/40 text-destructive hover:bg-destructive/10"
                onClick={() => {
                  setMotivoRechazo('');
                  setModalRechazar(true);
                }}
              >
                <XCircle className="mr-1.5 h-4 w-4" />
                Rechazar
              </Button>
              <Button
                type="button"
                size="sm"
                className="bg-emerald-600 text-white hover:bg-emerald-700 disabled:opacity-50"
                disabled={!expedienteCompleto || validar.isPending}
                title={
                  !expedienteCompleto
                    ? `Expediente incompleto (${faltantesCount} faltante${faltantesCount === 1 ? '' : 's'})`
                    : 'Validar expediente y activar proveedor'
                }
                onClick={() => setConfirmValidar(true)}
              >
                <CheckCircle2 className="mr-1.5 h-4 w-4" />
                Validar proveedor
              </Button>
            </>
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
            <Link to="/admin/datos-maestros/proveedores" aria-label="Cerrar">
              <X className="h-4 w-4" />
            </Link>
          </Button>
        </div>
      </header>

      <div className="px-4 pt-4 pb-6">
        {/* Banner informativo de En Revisión */}
        {enRevision && (
          <div className="mb-4 rounded-md border border-amber-300/60 bg-amber-500/10 p-3 text-amber-900 dark:text-amber-200">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="flex items-center gap-2 text-sm font-medium">
                <Clock className="h-4 w-4 shrink-0 text-amber-600 dark:text-amber-400" />
                <span>Proveedor en revisión por Cuentas por Pagar</span>
              </div>
              {expediente.data && (
                <span className="text-xs font-semibold">
                  {expedienteCompleto
                    ? '✓ Expediente documental completo'
                    : `Expediente incompleto: ${faltantesCount} documento${faltantesCount === 1 ? '' : 's'} faltante${faltantesCount === 1 ? '' : 's'}`}
                </span>
              )}
            </div>
            <p className="mt-1 text-xs text-muted-foreground">
              Las órdenes de compra, captura de facturas y programación de pagos están bloqueadas hasta que CxP valide el expediente.
            </p>
          </div>
        )}

        {/* Banner si el proveedor fue rechazado */}
        {proveedor.motivoRechazo && (
          <div className="mb-4 rounded-md border border-destructive/30 bg-destructive/10 p-3 text-destructive">
            <div className="flex items-center gap-2 text-sm font-semibold">
              <XCircle className="h-4 w-4 shrink-0" />
              Motivo de rechazo de CxP
            </div>
            <p className="mt-1 whitespace-pre-wrap text-xs text-foreground/90">
              {proveedor.motivoRechazo}
            </p>
          </div>
        )}

        <ProveedorDatosForm proveedor={proveedor} />
        <ProveedorBancariosSection proveedorId={proveedor.id} />
        <ProveedorExpedienteSection proveedorId={proveedor.id} />
      </div>

      {/* Diálogo de Confirmación para Validar Proveedor */}
      <AlertDialog open={confirmValidar} onOpenChange={setConfirmValidar}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Validar y activar proveedor</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas que el expediente documental de{' '}
              <span className="font-semibold text-foreground">
                {proveedor.razonSocial}
              </span>{' '}
              (<span className="font-mono">{proveedor.clave}</span>) ha sido
              revisado y cumple todos los requisitos de CxP? El proveedor pasará
              a estatus <span className="font-semibold text-emerald-600">Activo</span> y
              podrá operar con órdenes de compra y facturas.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={validar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={handleConfirmarValidar}
              disabled={validar.isPending}
              className="bg-emerald-600 hover:bg-emerald-700"
            >
              {validar.isPending ? 'Validando…' : 'Validar y activar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/* Modal para Capturar Motivo de Rechazo (5–500 car.) */}
      <Dialog open={modalRechazar} onOpenChange={setModalRechazar}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Rechazar proveedor</DialogTitle>
            <DialogDescription>
              Indica el motivo del rechazo del expediente para{' '}
              <span className="font-semibold text-foreground">
                {proveedor.razonSocial}
              </span>{' '}
              (<span className="font-mono">{proveedor.clave}</span>). El proveedor pasará
              a estatus Inactivo.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-2 py-2">
            <label
              htmlFor="motivo-rechazo"
              className="text-xs font-medium text-foreground"
            >
              Motivo de rechazo (obligatorio, 5 a 500 caracteres):
            </label>
            <Textarea
              id="motivo-rechazo"
              value={motivoRechazo}
              onChange={(e) => setMotivoRechazo(e.target.value)}
              placeholder="Ejemplo: La constancia de situación fiscal excede los 3 meses de vigencia y falta la carátula bancaria..."
              rows={4}
              maxLength={500}
              className="resize-none"
            />
            <div className="flex items-center justify-between text-xs text-muted-foreground">
              <span>
                {motivoTrim.length < 5 && motivoTrim.length > 0 && (
                  <span className="text-destructive">
                    Mínimo 5 caracteres ({5 - motivoTrim.length} faltante
                    {5 - motivoTrim.length === 1 ? '' : 's'})
                  </span>
                )}
              </span>
              <span>{motivoRechazo.length} / 500</span>
            </div>
          </div>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              disabled={rechazar.isPending}
              onClick={() => setModalRechazar(false)}
            >
              Cancelar
            </Button>
            <Button
              type="button"
              variant="destructive"
              disabled={!motivoValido || rechazar.isPending}
              onClick={handleConfirmarRechazar}
            >
              {rechazar.isPending ? 'Rechazando…' : 'Confirmar rechazo'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Diálogo de Confirmación para Desactivar */}
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

function EstatusBadge({ estatus }: { estatus: number }) {
  if (estatus === EstatusCatalogo.Activo) {
    return (
      <Badge
        variant="secondary"
        className="border-emerald-500/30 bg-emerald-500/15 text-emerald-700 dark:text-emerald-400"
      >
        Activo
      </Badge>
    );
  }
  if (estatus === EstatusCatalogo.EnRevision) {
    return (
      <Badge
        variant="outline"
        className="border-amber-400 bg-amber-500/15 font-medium text-amber-700 dark:border-amber-600 dark:text-amber-300"
      >
        En revisión
      </Badge>
    );
  }
  return (
    <Badge variant="outline" className="text-muted-foreground">
      Inactivo
    </Badge>
  );
}
