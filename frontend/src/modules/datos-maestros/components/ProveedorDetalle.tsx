import { useReactivarProveedor } from '../api/proveedores';
import { estadoUi } from '../lib/estados-ui';
import { PageHeader } from '@/components/erp/PageHeader';
import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest, codigoSoporte } from '@/lib/api';
import { ProveedorClasificacionFiscalSection } from './ProveedorClasificacionFiscalSection';
import { ProveedorToleranciaSection } from './ProveedorToleranciaSection';
import { Link, useParams } from '@tanstack/react-router';
import { CheckCircle2, Clock, PowerOff, TriangleAlert, X, XCircle } from 'lucide-react';
import { toast } from 'sonner';
import { Alert, AlertDescription } from '@/components/ui/alert';
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
  DialogBody,
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
  datosMaestrosKeys,
} from '@/modules/datos-maestros/api';
import { EstatusCatalogo } from '@/modules/datos-maestros/api/types';
import { useExpediente } from '@/components/erp/adjuntos/api';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ProveedorDatosForm } from '@/modules/datos-maestros/components/ProveedorDatosForm';
import { ProveedorCuentasBancariasSection } from '@/modules/datos-maestros/components/ProveedorCuentasBancariasSection';
import { ProveedorExpedienteSection } from '@/modules/datos-maestros/components/ProveedorExpedienteSection';
import { ProveedorBloqueo } from '@/modules/datos-maestros/components/ProveedorBloqueo';

/**
 * Detalle de proveedor (P3 del patrón cross-módulo). Header en flow
 * normal (NO sticky) con clave + razón social + badge de estatus +
 * acciones contextuales (Validar, Rechazar, Desactivar) + botón cerrar.
 * Sin tabs porque son pocas secciones — se renderizan directamente
 * <see cref="ProveedorDatosForm"/> y, si el usuario tiene el permiso
 * <c>bancarios-ver</c> (F1-ADM-05), <see cref="ProveedorCuentasBancariasSection"/> y, con <c>adjuntos-ver</c>
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
  const [revisionOpen, setRevisionOpen] = useState(false);
  const [motivoRevision, setMotivoRevision] = useState('');
  const cache = useQueryClient();
  const ponerRevision = useMutation({
    mutationFn: () =>
      apiRequest(`/api/v1/catalogos/proveedores/${id}/poner-revision`, {
        method: 'POST',
        body: { motivo: motivoRevision.trim() },
        idempotencyKey: crypto.randomUUID(),
      }),
    onSuccess: () => {
      void cache.invalidateQueries({ queryKey: datosMaestrosKeys.proveedores() });
      void cache.invalidateQueries({ queryKey: ['catalogos', 'proveedores'] });
      toast.success('Se puso al proveedor en revisión.');
      setRevisionOpen(false);
    },
    onError: (error) =>
      toast.error(
        esApiError(error)
          ? (error.problem.detail ?? error.problem.title)
          : 'No se pudo poner al proveedor en revisión.',
      ),
  });

  const canDesactivar = useHasPermission(PermisosCanonicos.CompartidoCatalogosAdministrar);
  const canValidar = useHasPermission(PermisosCanonicos.DatosMaestrosProveedoresValidar);
  const canVerAdjuntos = useHasPermission(PermisosCanonicos.DatosMaestrosProveedoresAdjuntosVer);

  const desactivar = useDesactivarProveedor();
  const reactivar = useReactivarProveedor();
  const validar = useValidarProveedor();
  const rechazar = useRechazarProveedor();

  const expediente = useExpediente(`/api/v1/datos-maestros/proveedores/${id}`, {
    enabled: canVerAdjuntos || canValidar,
  });

  if (proveedorQuery.isError) {
    const problem = esApiError(proveedorQuery.error) ? proveedorQuery.error.problem : undefined;
    return <ErrorState problem={problem} onRetry={() => proveedorQuery.refetch()} />;
  }

  if (proveedorQuery.isLoading || proveedorQuery.data == null) {
    return (
      <div className="space-y-5">
        <TableSkeleton
          rows={4}
          columns={[{ width: 'w-48' }, { width: 'w-64' }, { width: 'w-32' }]}
        />
      </div>
    );
  }

  const proveedor = proveedorQuery.data;
  const documentosPendientes = expediente.data
    ? [...expediente.data.faltantes, ...expediente.data.vencidos].map((codigo) =>
        expediente.data!.documentos.find((d) => d.codigo === codigo)?.nombre ?? codigo)
    : proveedor.documentosPendientes ?? [];
  const activo = proveedor.estatus === EstatusCatalogo.Activo;
  const enRevision = proveedor.estatus === EstatusCatalogo.EnRevision;

  const expedienteCompleto = expediente.data?.completo === true;
  const faltantesCount = expediente.data?.faltantes.length ?? 0;
  const vencidosCount = expediente.data?.vencidos.length ?? 0;
  // Faltantes y vencidos bloquean igual la validación: el aviso y el tooltip suman ambos.
  const pendientesCount = faltantesCount + vencidosCount;
  const textoPendientes = [
    faltantesCount > 0 ? `${faltantesCount} faltante${faltantesCount === 1 ? '' : 's'}` : null,
    vencidosCount > 0 ? `${vencidosCount} vencido${vencidosCount === 1 ? '' : 's'}` : null,
  ]
    .filter(Boolean)
    .join(' y ');
  const clasificado = proveedor.origen != null;

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
              description: codigoSoporte(error),
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
              description: error.problem.detail ?? codigoSoporte(error),
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
              description: error.problem.detail ?? codigoSoporte(error),
            });
          } else {
            toast.error('Error al rechazar el proveedor.');
          }
        },
      },
    );
  }

  return (
    <div className="flex flex-col gap-5">
      {/* Header NO sticky: viaja con el scroll para no tapar la primera
          fila del form al anclar. */}
      {activo && documentosPendientes.length > 0 && (
        <Alert variant="warning">
          <TriangleAlert className="size-4" aria-hidden="true" />
          <AlertDescription>
            Expediente incompleto: faltan {documentosPendientes.length} documentos ({documentosPendientes.join(', ')}).
            {proveedor.origenCarga === 'CargaSap' && ' Cargado desde SAP sin documentos.'}{' '}
            <a href="#expediente-proveedor" className="underline underline-offset-2">Ver expediente</a>
          </AlertDescription>
        </Alert>
      )}
      <PageHeader
        mono
        titulo={<>{proveedor.clave}</>}
        descripcion={<>{proveedor.razonSocial}</>}
        estado={
          <>
            <EstatusBadge estatus={proveedor.estatus} />
            {proveedor.bloqueado === true && <Badge variant="danger">Bloqueado</Badge>}
          </>
        }
        acciones={
          <>
            {/* Acciones de CxP para proveedores En Revisión (G1.1) */}
            {enRevision && canValidar && (
              <>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="border-danger/40 text-danger-fg hover:bg-danger-bg/10"
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
                  className="bg-success text-primary-foreground hover:bg-success-fg disabled:opacity-50"
                  disabled={!expedienteCompleto || !clasificado || validar.isPending}
                  title={
                    !clasificado
                      ? 'Clasifica al proveedor (origen y régimen fiscal) antes de validarlo'
                      : !expedienteCompleto
                        ? `Expediente incompleto (${textoPendientes || 'documentos pendientes'})`
                        : 'Validar expediente y activar proveedor'
                  }
                  onClick={() => setConfirmValidar(true)}
                >
                  <CheckCircle2 className="mr-1.5 h-4 w-4" />
                  Validar proveedor
                </Button>
              </>
            )}

            {canDesactivar && proveedor.estatus === EstatusCatalogo.Inactivo && (
              <Button variant="ghost" size="sm" disabled={reactivar.isPending} onClick={() => reactivar.mutate({ id: proveedor.id, idempotencyKey: crypto.randomUUID() }, { onError: e => toast.error(e.message) })}>
                {reactivar.isPending ? 'Reactivando…' : 'Reactivar'}
              </Button>
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
          </>
        }
      />
      <dl className="grid grid-cols-2 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat">
        <div>
          <dt className="text-xs text-ink-muted">RFC</dt>
          <dd className="font-mono text-sm">{proveedor.rfc ?? '—'}</dd>
        </div>
        <div>
          <dt className="text-xs text-ink-muted">Clave de A+W</dt>
          <dd className="font-mono text-sm">{proveedor.claveAw ?? '—'}</dd>
        </div>
        <div>
          <dt className="text-xs text-ink-muted">Días de crédito</dt>
          <dd className="font-mono text-sm">{proveedor.condicionesPagoDias ?? '—'}</dd>
        </div>
      </dl>

      <div className="space-y-5">
        <ProveedorBloqueo proveedor={proveedor} />
        {/* Banner informativo de En Revisión */}
        {enRevision && (
          <div className="mb-4 rounded-md bg-warning-note-bg p-3 text-warning-note-fg">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="flex items-center gap-2 text-sm font-medium">
                <Clock className="h-4 w-4 shrink-0 text-warning" />
                <span>Proveedor en revisión por Cuentas por Pagar</span>
              </div>
              {expediente.data && (
                <span className="text-xs font-semibold">
                  {expedienteCompleto
                    ? '✓ Expediente documental completo'
                    : `Expediente incompleto: ${textoPendientes || `${pendientesCount} documentos pendientes`}`}
                </span>
              )}
            </div>
            <p className="mt-1 text-xs text-ink-muted">
              Las órdenes de compra, captura de facturas y programación de pagos están bloqueadas
              hasta que CxP valide el expediente.
            </p>
          </div>
        )}

        {/* Banner si el proveedor fue rechazado */}
        {proveedor.motivoRechazo && (
          <div className="mb-4 rounded-md border border-danger/30 bg-danger-bg/10 p-3 text-danger-fg">
            <div className="flex items-center gap-2 text-sm font-semibold">
              <XCircle className="h-4 w-4 shrink-0" />
              Motivo de rechazo de CxP
            </div>
            <p className="mt-1 whitespace-pre-wrap text-xs text-ink/90">
              {proveedor.motivoRechazo}
            </p>
          </div>
        )}

        {canValidar && proveedor.estatus === EstatusCatalogo.Activo && (
          <Button variant="secondary" onClick={() => setRevisionOpen(true)}>
            Poner en revisión
          </Button>
        )}
        <Dialog open={revisionOpen} onOpenChange={setRevisionOpen}>
          <DialogContent size="sm">
            <DialogHeader>
              <DialogTitle>Poner proveedor en revisión</DialogTitle>
              <DialogDescription>
                El proveedor dejará de estar disponible para nuevas órdenes de compra. El motivo se
                guardará en la bitácora.
              </DialogDescription>
            </DialogHeader>
            <DialogBody>
              <label htmlFor="motivo-revision-proveedor" className="text-sm text-ink">
                Motivo de revisión
              </label>
              <Textarea
                id="motivo-revision-proveedor"
                value={motivoRevision}
                maxLength={500}
                onChange={(e) => setMotivoRevision(e.target.value)}
              />
            </DialogBody>
            <DialogFooter>
              <Button variant="ghost" onClick={() => setRevisionOpen(false)}>
                Cancelar
              </Button>
              <Button
                disabled={motivoRevision.trim().length < 5 || ponerRevision.isPending}
                onClick={() => ponerRevision.mutate()}
              >
                Poner en revisión
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
        <ProveedorDatosForm proveedor={proveedor} />
        <ProveedorClasificacionFiscalSection
          key={`fiscal-${proveedor.id}`}
          proveedorId={proveedor.id}
          origen={proveedor.origen ?? null}
          regimenFiscalCodigo={proveedor.regimenFiscalCodigo ?? null}
          conceptoRetencion={proveedor.conceptoRetencion ?? null}
          tipoPersona={proveedor.tipoPersona}
        />
        <ProveedorToleranciaSection
          key={proveedor.id}
          proveedorId={proveedor.id}
          montoMxn={proveedor.toleranciaFacturaContraOcMxn}
        />
        <ProveedorCuentasBancariasSection
          proveedorId={proveedor.id}
          razonSocial={proveedor.razonSocial}
        />
        <ProveedorExpedienteSection proveedorId={proveedor.id} origen={proveedor.origen ?? null} />
      </div>

      {/* Diálogo de Confirmación para Validar Proveedor */}
      <AlertDialog open={confirmValidar} onOpenChange={setConfirmValidar}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Validar y activar proveedor</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas que el expediente documental de{' '}
              <span className="font-semibold text-ink">{proveedor.razonSocial}</span> (
              <span className="font-mono">{proveedor.clave}</span>) ha sido revisado y cumple todos
              los requisitos de CxP? El proveedor pasará a estatus{' '}
              <span className="font-semibold text-success-fg">Activo</span> y podrá operar con
              órdenes de compra y facturas.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={validar.isPending}>Cancelar</AlertDialogCancel>
            <AlertDialogAction
              onClick={handleConfirmarValidar}
              disabled={validar.isPending}
              className="bg-success text-primary-foreground hover:bg-success-fg"
            >
              {validar.isPending ? 'Validando…' : 'Validar y activar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/* Modal para Capturar Motivo de Rechazo (5–500 car.) */}
      <Dialog open={modalRechazar} onOpenChange={setModalRechazar}>
        <DialogContent size="sm">
          <DialogHeader>
            <DialogTitle>Rechazar proveedor</DialogTitle>
            <DialogDescription>
              Indica el motivo del rechazo del expediente para{' '}
              <span className="font-semibold text-ink">{proveedor.razonSocial}</span> (
              <span className="font-mono">{proveedor.clave}</span>). El proveedor pasará a estatus
              Inactivo.
            </DialogDescription>
          </DialogHeader>

          <DialogBody>
            <div className="space-y-2 py-2">
              <label htmlFor="motivo-rechazo" className="text-xs font-medium text-ink">
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
              <div className="flex items-center justify-between text-xs text-ink-muted">
                <span>
                  {motivoTrim.length < 5 && motivoTrim.length > 0 && (
                    <span className="text-danger-fg">
                      Mínimo 5 caracteres ({5 - motivoTrim.length} faltante
                      {5 - motivoTrim.length === 1 ? '' : 's'})
                    </span>
                  )}
                </span>
                <span>{motivoRechazo.length} / 500</span>
              </div>
            </div>
          </DialogBody>

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
      <AlertDialog open={confirmDesactivar} onOpenChange={setConfirmDesactivar}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar proveedor</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar al proveedor{' '}
              <span className="font-mono font-semibold">{proveedor.clave}</span>? Las requisiciones
              históricas se conservan. El artículo deja de estar disponible para nuevas
              requisiciones.
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

function EstatusBadge({ estatus }: { estatus: number }) {
  const ui = estadoUi(
    'catalogo',
    estatus === EstatusCatalogo.Activo
      ? 'Activo'
      : estatus === EstatusCatalogo.EnRevision
        ? 'EnRevision'
        : 'Inactivo',
  );
  return <Badge variant={ui.tono}>{ui.texto}</Badge>;
}
