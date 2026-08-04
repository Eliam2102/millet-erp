import { useState } from 'react';
import { Ban, Lock, ShoppingCart, Send, ShieldCheck, Trash2, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { DateTimeDisplay } from '@/components/erp';
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
  accionAprobarNivel1,
  accionAprobarNivel2,
  accionCancelar,
  accionCerrarManual,
  accionConvertirAOc,
  accionEliminarRequisicion,
  accionRechazar,
  accionTransmitir,
} from '@/features/compras/lib/acciones-disponibles';
import {
  EstadoRequisicion,
  NivelAutorizacion,
  OrigenRequisicion,
  type RequisicionResponse,
} from '@/features/compras/api/types';
import {
  useAutorizarRequisicion,
  useCancelarRequisicion,
  useCerrarManualRequisicion,
  useEliminarRequisicion,
  useRechazarRequisicion,
  useTransmitirRequisicion,
} from '@/features/compras/api/useWorkflow';
import { ModalMotivo } from '@/features/compras/components/ModalMotivo';
import { esApiError } from '@/lib/api';
import { esConflictoConcurrencia } from '@/lib/api/error';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useAutoGenerarOcAlAutorizar } from '@/lib/auth/useComprasSettings';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { useNuevaOrdenCompra } from '@/features/compras/ordenes/components/nueva-orden-compra-context';
import { useQueryClient } from '@tanstack/react-query';
import { comprasKeys } from '@/features/compras/api/keys';

/**
 * <c>&lt;AccionesRequisicion/&gt;</c> — barra de botones contextuales
 * en P3 detalle, gateados por la matriz §6.1 vía
 * <c>acciones-disponibles.ts</c>. Doc 05 §6 + §13.3.
 *
 * <para>Acciones soportadas:</para>
 * <list>
 *   <item><b>Transmitir</b> (Borrador → EnAutorización) con confirm
 *   simple. Disabled si la RQ tiene 0 líneas (tooltip explicativo).</item>
 *   <item><b>Aprobar Nivel1</b> / <b>Aprobar Nivel2</b> (registrar
 *   firma) con confirm simple. N2 disabled con tooltip si N1 no
 *   firmó aún.</item>
 *   <item><b>Rechazar</b> (EnAutorización → Rechazada) abre
 *   <c>&lt;ModalMotivo&gt;</c> con <c>aplicaA=Rechazo</c>.</item>
 *   <item><b>Eliminar</b> (Borrador / EnAutorización → Eliminada,
 *   UF4-PR1) abre <c>&lt;ModalMotivo&gt;</c> con
 *   <c>aplicaA=Eliminacion</c>. <b>Sin Idempotency-Key</b> (doc 05
 *   §7.6).</item>
 *   <item><b>Cancelar</b> (Autorizada / EnSurtido → Cancelada,
 *   UF4-PR1) abre <c>&lt;ModalMotivo&gt;</c> con
 *   <c>aplicaA=Cancelacion</c>. <b>Con Idempotency-Key</b>; maneja
 *   <c>422 CANCELAR_FALLO</c> (efecto downstream — A+W o reservas)
 *   con toast específico que incluye <c>traceId</c>; sin retry
 *   automático (el usuario decide tras revisar logs).</item>
 * </list>
 */
export interface AccionesRequisicionProps {
  rq: RequisicionResponse;
}

type ConfirmacionSimple =
  | { tipo: 'transmitir' }
  | { tipo: 'aprobar'; nivel: NivelAutorizacion }
  | null;

export function AccionesRequisicion({ rq }: AccionesRequisicionProps) {
  const permisos = useAuthStore((s) => s.permisos);
  const userId = useAuthStore((s) => s.user?.id) ?? '';
  const autoGenerarOcAlAutorizar = useAutoGenerarOcAlAutorizar();

  const transmitir = useTransmitirRequisicion();
  const autorizar = useAutorizarRequisicion();
  const rechazar = useRechazarRequisicion();
  const eliminar = useEliminarRequisicion();
  const cancelar = useCancelarRequisicion();
  const cerrarManual = useCerrarManualRequisicion();
  // Idempotencia: cada acción genera su PROPIA key fresca por submit
  // (crypto.randomUUID() dentro del .mutate), NO una compartida por montaje.
  // Una key estable reusada entre acciones con bodies distintos (N1 vs N2,
  // cancelar…) daba 422 IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY. Patrón
  // AccionesOC; las acciones de RQ no mueven dinero y tienen guarda de dominio.
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();
  const nuevaOc = useNuevaOrdenCompra();

  const [confirmacion, setConfirmacion] = useState<ConfirmacionSimple>(null);
  const [modalRechazoAbierto, setModalRechazoAbierto] = useState(false);
  const [modalEliminarAbierto, setModalEliminarAbierto] = useState(false);
  const [modalCancelarAbierto, setModalCancelarAbierto] = useState(false);
  const [modalCerrarManualAbierto, setModalCerrarManualAbierto] = useState(false);

  const accTransmitir = accionTransmitir(rq, permisos);
  const accN1 = accionAprobarNivel1(rq, permisos);
  const accN2 = accionAprobarNivel2(rq, permisos, userId);

  // Chip informativo "Nivel 1 autorizado": cuando la RQ está EnAutorizacion
  // y N1 ya está firmado (la firma es única por nivel). Reemplaza visualmente
  // al botón N1 (que ahora se oculta) para que el autorizador vea que N1 está
  // hecho y solo le toca N2. Solo en EnAutorizacion: en estados posteriores
  // la fase de autorización ya terminó y el chip sería ruido.
  const n1Autorizada =
    rq.estado === EstadoRequisicion.EnAutorizacion
      ? rq.autorizaciones.find((a) => a.nivel === NivelAutorizacion.Nivel1)
      : undefined;
  const accRechazar = accionRechazar(rq, permisos);
  const accEliminar = accionEliminarRequisicion(rq, permisos);
  const accCancelar = accionCancelar(rq, permisos);
  const accCerrarManual = accionCerrarManual(rq, permisos);
  const accConvertirAOc = accionConvertirAOc(
    rq,
    permisos,
    autoGenerarOcAlAutorizar,
  );

  // ADR-0043: "piezas en vuelo" para el aviso del cierre manual — saldo a
  // compra aún no recibido, derivado de la propia RQ (sin consultar la OC).
  const piezasEnVuelo = rq.lineas.reduce(
    (acc, l) => acc + Math.max(0, l.cantDeCompra - l.cantRecibida),
    0,
  );
  const avisoOcEnVuelo =
    piezasEnVuelo > 0
      ? `Esta RQ tiene una OC en vuelo por ${piezasEnVuelo} pieza(s) que llegarán como stock (no se cancelan al cerrar).`
      : null;

  const algunaVisible =
    accTransmitir.visible ||
    accN1.visible ||
    accN2.visible ||
    accRechazar.visible ||
    accEliminar.visible ||
    accCancelar.visible ||
    accCerrarManual.visible ||
    accConvertirAOc.visible;

  if (!algunaVisible) return null;

  /**
   * 409 <c>CONCURRENCY_CONFLICT</c> abre el dialog en modo simple
   * (acciones sin form local). Otros errores caen al toast genérico.
   * Cierra el confirm dialog que estaba abierto para que el conflict
   * dialog tenga foco solo (un solo modal a la vez).
   */
  function manejarError(error: Error, accionLabel: string) {
    if (esConflictoConcurrencia(error)) {
      setConfirmacion(null);
      setModalRechazoAbierto(false);
      setModalEliminarAbierto(false);
      setModalCancelarAbierto(false);
      setModalCerrarManualAbierto(false);
      conflictDialog.openSimple({
        traceId: error.traceId,
        onRefrescar: () =>
          queryClient.invalidateQueries({
            queryKey: comprasKeys.requisicion(rq.id),
          }),
      });
      return;
    }
    if (esApiError(error)) {
      toast.error(`No se pudo ${accionLabel.toLowerCase()}`, {
        description: error.problem.detail ?? error.problem.title,
      });
    } else {
      toast.error(`Error inesperado al ${accionLabel.toLowerCase()}.`);
    }
  }

  function ejecutarTransmitir() {
    transmitir.mutate(
      { requisicionId: rq.id, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success('Requisición transmitida a autorización');
          setConfirmacion(null);
        },
        onError: (e) => manejarError(e, 'transmitir'),
      },
    );
  }

  function ejecutarAprobar(nivel: NivelAutorizacion) {
    autorizar.mutate(
      {
        requisicionId: rq.id,
        values: { nivel, notas: null },
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success(
            `Autorización Nivel ${nivel === NivelAutorizacion.Nivel1 ? '1' : '2'} registrada`,
          );
          setConfirmacion(null);
        },
        onError: (e) => manejarError(e, 'aprobar'),
      },
    );
  }

  function ejecutarRechazar(args: { motivoId: string; motivoTexto: string | null }) {
    rechazar.mutate(
      {
        requisicionId: rq.id,
        values: { motivoId: args.motivoId, motivoTexto: args.motivoTexto },
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success('Requisición rechazada');
          setModalRechazoAbierto(false);
        },
        onError: (e) => manejarError(e, 'rechazar'),
      },
    );
  }

  function ejecutarEliminar(args: { motivoId: string; motivoTexto: string | null }) {
    eliminar.mutate(
      {
        requisicionId: rq.id,
        values: { motivoId: args.motivoId, motivoTexto: args.motivoTexto },
      },
      {
        onSuccess: () => {
          toast.success('Requisición eliminada');
          setModalEliminarAbierto(false);
        },
        onError: (e) => manejarError(e, 'eliminar'),
      },
    );
  }

  /**
   * <c>CANCELAR_FALLO</c> (422): el dominio aceptó el comando pero un
   * efecto downstream falló (A+W, liberar reservas, etc.). UX:
   * mensaje específico con <c>traceId</c> visible + CTA <b>Reintentar</b>
   * en el toast (no automático, doc 05 §7.6 — el usuario debe entender
   * qué pasó downstream antes de reintentar).
   */
  function ejecutarCancelar(args: { motivoId: string; motivoTexto: string | null }) {
    const valuesParaReintentar = {
      motivoId: args.motivoId,
      motivoTexto: args.motivoTexto,
    };
    cancelar.mutate(
      {
        requisicionId: rq.id,
        values: valuesParaReintentar,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success('Requisición cancelada', {
            description:
              'Reservas liberadas y OCs borrador asociadas canceladas.',
          });
          setModalCancelarAbierto(false);
        },
        onError: (error) => {
          if (
            esApiError(error) &&
            error.code === 'CANCELAR_FALLO' &&
            error.status === 422
          ) {
            setModalCancelarAbierto(false);
            toast.error('La cancelación falló downstream', {
              description: error.traceId
                ? `Algunos efectos no se aplicaron (reservas / OCs / A+W). Revisa con soporte. Código: ${error.traceId}`
                : 'Algunos efectos no se aplicaron (reservas / OCs / A+W). Revisa con soporte antes de reintentar.',
              duration: 15000,
              action: {
                label: 'Reintentar',
                onClick: () => {
                  setModalCancelarAbierto(true);
                  // El usuario verá el ModalMotivo abierto con su
                  // selección previa perdida — es deliberado: queremos
                  // que reconfirme el motivo después de un fallo
                  // downstream.
                },
              },
            });
            return;
          }
          manejarError(error, 'cancelar');
        },
      },
    );
  }

  function ejecutarCerrarManual(args: { motivoId: string; motivoTexto: string | null }) {
    cerrarManual.mutate(
      {
        requisicionId: rq.id,
        values: { motivoId: args.motivoId, motivoTexto: args.motivoTexto },
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success('Requisición cerrada', {
            description:
              'El material no entregado quedó disponible como stock.',
          });
          setModalCerrarManualAbierto(false);
        },
        onError: (error) => {
          if (
            esApiError(error) &&
            error.code === 'CIERRE_MANUAL_FALLO' &&
            error.status === 422
          ) {
            setModalCerrarManualAbierto(false);
            toast.error('El cierre manual falló downstream', {
              description: error.traceId
                ? `Algunos efectos no se aplicaron (liberación de reservas). Revisa con soporte. Código: ${error.traceId}`
                : 'Algunos efectos no se aplicaron (liberación de reservas). Revisa con soporte antes de reintentar.',
              duration: 15000,
              action: {
                label: 'Reintentar',
                onClick: () => setModalCerrarManualAbierto(true),
              },
            });
            return;
          }
          manejarError(error, 'cerrar');
        },
      },
    );
  }

  const isPending =
    transmitir.isPending ||
    autorizar.isPending ||
    rechazar.isPending ||
    eliminar.isPending ||
    cancelar.isPending ||
    cerrarManual.isPending;

  return (
    <>
      <div
        className="flex flex-wrap items-center gap-2"
        role="toolbar"
        aria-label="Acciones de la requisición"
      >
        {accTransmitir.visible && (
          <Button
            type="button"
            disabled={!accTransmitir.habilitada || isPending}
            onClick={() => setConfirmacion({ tipo: 'transmitir' })}
            title={accTransmitir.motivoDeshabilitada}
          >
            <Send className="mr-2 h-4 w-4" />
            Transmitir
          </Button>
        )}
        {accN1.visible && (
          <Button
            type="button"
            variant="default"
            disabled={!accN1.habilitada || isPending}
            onClick={() =>
              setConfirmacion({ tipo: 'aprobar', nivel: NivelAutorizacion.Nivel1 })
            }
            title={accN1.motivoDeshabilitada}
            className="bg-emerald-600 text-white hover:bg-emerald-700"
          >
            <ShieldCheck className="mr-2 h-4 w-4" />
            Aprobar Nivel 1
          </Button>
        )}
        {n1Autorizada && (
          <Badge
            variant="outline"
            className="gap-1 border-emerald-300 bg-emerald-50 text-emerald-700"
            data-n1-autorizada=""
          >
            <ShieldCheck className="h-3.5 w-3.5" />
            Nivel 1 autorizado
            <span className="font-normal text-emerald-600">
              <DateTimeDisplay value={n1Autorizada.fechaHora} />
            </span>
          </Badge>
        )}
        {accN2.visible && (
          <Button
            type="button"
            variant="default"
            disabled={!accN2.habilitada || isPending}
            onClick={() =>
              setConfirmacion({ tipo: 'aprobar', nivel: NivelAutorizacion.Nivel2 })
            }
            title={accN2.motivoDeshabilitada}
            className={
              accN2.habilitada
                ? 'bg-emerald-600 text-white hover:bg-emerald-700'
                : undefined
            }
          >
            <ShieldCheck className="mr-2 h-4 w-4" />
            Aprobar Nivel 2
          </Button>
        )}
        {accRechazar.visible && (
          <Button
            type="button"
            variant="outline"
            disabled={!accRechazar.habilitada || isPending}
            onClick={() => setModalRechazoAbierto(true)}
            className="border-rose-300 text-rose-700 hover:bg-rose-50"
          >
            <X className="mr-2 h-4 w-4" />
            Rechazar
          </Button>
        )}
        {accEliminar.visible && (
          <Button
            type="button"
            variant="outline"
            disabled={!accEliminar.habilitada || isPending}
            onClick={() => setModalEliminarAbierto(true)}
            className="border-rose-300 text-rose-700 hover:bg-rose-50"
          >
            <Trash2 className="mr-2 h-4 w-4" />
            Eliminar
          </Button>
        )}
        {accCancelar.visible && (
          <Button
            type="button"
            variant="outline"
            disabled={!accCancelar.habilitada || isPending}
            onClick={() => setModalCancelarAbierto(true)}
            className="border-amber-300 text-amber-700 hover:bg-amber-50"
          >
            <Ban className="mr-2 h-4 w-4" />
            Cancelar
          </Button>
        )}
        {accCerrarManual.visible && (
          <Button
            type="button"
            variant="outline"
            disabled={!accCerrarManual.habilitada || isPending}
            onClick={() => setModalCerrarManualAbierto(true)}
            className="border-zinc-300 text-zinc-700 hover:bg-zinc-100"
          >
            <Lock className="mr-2 h-4 w-4" />
            Cerrar
          </Button>
        )}
        {accConvertirAOc.visible && (
          <Button
            type="button"
            disabled={!accConvertirAOc.habilitada || isPending}
            onClick={() =>
              nuevaOc.abrir({
                desdeRequisicion: {
                  requisicionId: rq.id,
                  sucursalId: rq.sucursalId,
                  folio: rq.folio,
                },
              })
            }
            title={accConvertirAOc.motivoDeshabilitada}
            className="bg-indigo-600 text-white hover:bg-indigo-700"
            data-action="convertir-a-oc"
          >
            <ShoppingCart className="mr-2 h-4 w-4" />
            Convertir a OC
          </Button>
        )}
      </div>

      {/* Confirm simple para Transmitir / Aprobar (no requiere motivo) */}
      <AlertDialog
        open={confirmacion != null}
        onOpenChange={(open) => {
          if (!open && !isPending) setConfirmacion(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {confirmacion?.tipo === 'transmitir'
                ? `¿Transmitir requisición ${rq.folio}?`
                : confirmacion?.tipo === 'aprobar'
                  ? `¿Aprobar Nivel ${confirmacion.nivel === NivelAutorizacion.Nivel1 ? '1' : '2'} de ${rq.folio}?`
                  : null}
            </AlertDialogTitle>
            <AlertDialogDescription>
              {confirmacion?.tipo === 'transmitir' &&
                'La RQ pasará a EnAutorización y los autorizadores podrán firmarla. No podrás editar líneas estructuralmente después.'}
              {confirmacion?.tipo === 'aprobar' &&
                'Tu firma queda registrada con tu usuario y la fecha actual. No puedes deshacerla; solo se puede rechazar la RQ por un autorizador.'}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={isPending}>Cancelar</AlertDialogCancel>
            <AlertDialogAction
              disabled={isPending}
              onClick={() => {
                if (confirmacion?.tipo === 'transmitir') ejecutarTransmitir();
                else if (confirmacion?.tipo === 'aprobar')
                  ejecutarAprobar(confirmacion.nivel);
              }}
            >
              {isPending ? 'Procesando…' : 'Confirmar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/* Modal con motivo para Rechazar */}
      <ModalMotivo
        open={modalRechazoAbierto}
        onOpenChange={(open) => {
          if (!isPending) setModalRechazoAbierto(open);
        }}
        variante="rechazar"
        folio={rq.folio}
        onConfirm={ejecutarRechazar}
        isPending={rechazar.isPending}
      />

      {/* Modal con motivo para Eliminar (pre-aut). Para RQs de sistema
          (reabasto, ADR-0047 PR5) avisa que el motor podría re-proponerla. */}
      <ModalMotivo
        open={modalEliminarAbierto}
        onOpenChange={(open) => {
          if (!isPending) setModalEliminarAbierto(open);
        }}
        variante="eliminar"
        folio={rq.folio}
        onConfirm={ejecutarEliminar}
        isPending={eliminar.isPending}
        aviso={
          rq.origen === OrigenRequisicion.Sistema
            ? 'Esta requisición la creó el motor de reabasto. Si el artículo sigue por debajo del objetivo, volverá a proponerla en el próximo barrido.'
            : undefined
        }
      />

      {/* Modal con motivo para Cancelar (post-aut) */}
      <ModalMotivo
        open={modalCancelarAbierto}
        onOpenChange={(open) => {
          if (!isPending) setModalCancelarAbierto(open);
        }}
        variante="cancelar"
        folio={rq.folio}
        onConfirm={ejecutarCancelar}
        isPending={cancelar.isPending}
      />

      {/* Modal con motivo para Cierre manual (ADR-0043), con aviso de OC en vuelo */}
      <ModalMotivo
        open={modalCerrarManualAbierto}
        onOpenChange={(open) => {
          if (!isPending) setModalCerrarManualAbierto(open);
        }}
        variante="cerrar-manual"
        folio={rq.folio}
        onConfirm={ejecutarCerrarManual}
        isPending={cerrarManual.isPending}
        aviso={avisoOcEnVuelo}
      />
    </>
  );
}
