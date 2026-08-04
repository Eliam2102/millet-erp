import { useState } from 'react';
import { useNavigate } from '@tanstack/react-router';
import { useQueryClient } from '@tanstack/react-query';
import { Send, CheckCircle2, XCircle, Loader2, Ban, Copy } from 'lucide-react';
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
import { useFormIdempotencyKey, esApiError } from '@/lib/api';
import { hoyLocalISO } from '@/lib/datetime';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { handleOcMutationError } from '@/features/compras/ordenes/lib/handle-conflict';
import {
  accionTransmitirAAutorizacion,
  accionAprobarNivel1,
  accionAprobarNivel2,
  accionRechazar,
  accionCancelar1Firma,
  accionCancelarDobleFirma,
  accionDuplicarOc,
} from '@/features/compras/ordenes/lib/acciones-disponibles';
import type { OrdenCompraDetalleResponse } from '@/features/compras/ordenes/api/types';
import { useEnviarAAutorizacion } from '@/features/compras/ordenes/api/useEnviarAAutorizacion';
import { useAutorizarOrdenCompra } from '@/features/compras/ordenes/api/useAutorizarOrdenCompra';
import { useRechazarOrdenCompra } from '@/features/compras/ordenes/api/useRechazarOrdenCompra';
import { useCancelarOrdenCompra } from '@/features/compras/ordenes/api/useCancelarOrdenCompra';
import { useCancelarOrdenCompraConRecepciones } from '@/features/compras/ordenes/api/useCancelarOrdenCompraConRecepciones';
import { useDuplicarOrdenCompra } from '@/features/compras/ordenes/api/useDuplicarOrdenCompra';
import {
  ModalMotivoOC,
  type ModalMotivoValues,
} from '@/features/compras/ordenes/components/ModalMotivoOC';
import { DobleFirmaDialog } from '@/features/compras/ordenes/components/DobleFirmaDialog';
import { ConfirmDuplicarDialog } from '@/features/compras/ordenes/components/ConfirmDuplicarDialog';
import { MotivoRechazoAplicaA } from '@/features/compras/api/types';
import { estadoOcToString } from '@/features/compras/ordenes/api/types';
import { NivelAutorizacion } from '@/features/compras/ordenes/schemas/autorizar';
import type { CancelarOcConRecepcionesValues } from '@/features/compras/ordenes/schemas/cancelar-doble-firma';

/**
 * <c>&lt;AccionesOC/&gt;</c> — botones contextuales de workflow en la
 * página de detalle de OC (UF4-PR1). Todos gateados por la matriz §6.1:
 *
 * <list>
 *   <item><b>Transmitir</b> en <c>Borrador</c>/<c>Rechazada</c>:
 *   confirm dialog con resumen, dispara
 *   <c>POST /transmitir</c>.</item>
 *   <item><b>Aprobar Nivel 1</b> en
 *   <c>EnAutorizacionJefeCompras</c>: confirm + POST
 *   <c>/autorizaciones</c> con <c>nivel=Nivel1</c>.</item>
 *   <item><b>Aprobar Nivel 2</b> en
 *   <c>EnAutorizacionDireccion</c>: igual con <c>nivel=Nivel2</c>.</item>
 *   <item><b>Rechazar</b> en cualquier <c>EnAutorizacion*</c>:
 *   abre <c>&lt;ModalMotivoOC/&gt;</c> que captura motivo + texto +
 *   notas. POST <c>/rechazar</c>.</item>
 * </list>
 *
 * <para>Cada acción regenera su propia <c>Idempotency-Key</c> en cada
 * apertura del confirm dialog (re-mount via <c>key</c> prop) para
 * evitar 409 IDEMPOTENCY_IN_PROGRESS entre intentos sucesivos — mismo
 * fix que UF2-PR3-c en LineaDialog.</para>
 */
export interface AccionesOCProps {
  oc: OrdenCompraDetalleResponse;
}

type ConfirmAction = 'transmitir' | 'aprobar-n1' | 'aprobar-n2' | null;

export function AccionesOC({ oc }: AccionesOCProps) {
  const permisos = useAuthStore((s) => s.permisos);
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const transmitirMut = useEnviarAAutorizacion();
  const autorizarMut = useAutorizarOrdenCompra();
  const rechazarMut = useRechazarOrdenCompra();
  const cancelarMut = useCancelarOrdenCompra();
  const cancelarDobleMut = useCancelarOrdenCompraConRecepciones();
  const duplicarMut = useDuplicarOrdenCompra();

  const accTransmitir = accionTransmitirAAutorizacion(oc, permisos);
  const accN1 = accionAprobarNivel1(oc, permisos);
  const accN2 = accionAprobarNivel2(oc, permisos);
  const accRechazar = accionRechazar(oc, permisos);
  const accCancelar1 = accionCancelar1Firma(oc, permisos);
  const accCancelarDoble = accionCancelarDobleFirma(oc, permisos);
  const accDuplicar = accionDuplicarOc(oc, permisos);

  const [confirmAction, setConfirmAction] = useState<ConfirmAction>(null);
  const [confirmInstance, setConfirmInstance] = useState(0);
  const [rechazarOpen, setRechazarOpen] = useState(false);
  const [rechazarInstance, setRechazarInstance] = useState(0);
  const [cancelar1Open, setCancelar1Open] = useState(false);
  const [cancelar1Instance, setCancelar1Instance] = useState(0);
  const [cancelarDobleOpen, setCancelarDobleOpen] = useState(false);
  const [cancelarDobleInstance, setCancelarDobleInstance] = useState(0);
  const [duplicarOpen, setDuplicarOpen] = useState(false);
  const [duplicarInstance, setDuplicarInstance] = useState(0);

  function abrirConfirm(action: ConfirmAction) {
    setConfirmInstance((n) => n + 1);
    setConfirmAction(action);
  }
  function abrirRechazar() {
    setRechazarInstance((n) => n + 1);
    setRechazarOpen(true);
  }
  function abrirCancelar1() {
    setCancelar1Instance((n) => n + 1);
    setCancelar1Open(true);
  }
  function abrirCancelarDoble() {
    setCancelarDobleInstance((n) => n + 1);
    setCancelarDobleOpen(true);
  }
  function abrirDuplicar() {
    setDuplicarInstance((n) => n + 1);
    setDuplicarOpen(true);
  }

  if (
    !accTransmitir.visible &&
    !accN1.visible &&
    !accN2.visible &&
    !accRechazar.visible &&
    !accCancelar1.visible &&
    !accCancelarDoble.visible &&
    !accDuplicar.visible
  ) {
    return null;
  }

  const isPending =
    transmitirMut.isPending ||
    autorizarMut.isPending ||
    rechazarMut.isPending ||
    cancelarMut.isPending ||
    cancelarDobleMut.isPending ||
    duplicarMut.isPending;

  return (
    <div
      className="flex flex-wrap items-center gap-2"
      data-component="acciones-oc"
    >
      {accTransmitir.visible && (
        <Button
          size="sm"
          variant="default"
          onClick={() => abrirConfirm('transmitir')}
          disabled={!accTransmitir.habilitada || isPending}
          title={accTransmitir.motivoDeshabilitada}
          data-action="transmitir"
        >
          <Send className="mr-1 h-3.5 w-3.5" />
          Transmitir a autorización
        </Button>
      )}
      {accN1.visible && (
        <Button
          size="sm"
          variant="default"
          onClick={() => abrirConfirm('aprobar-n1')}
          disabled={!accN1.habilitada || isPending}
          title={accN1.motivoDeshabilitada}
          data-action="aprobar-n1"
        >
          <CheckCircle2 className="mr-1 h-3.5 w-3.5" />
          Aprobar Nivel 1
        </Button>
      )}
      {accN2.visible && (
        <Button
          size="sm"
          variant="default"
          onClick={() => abrirConfirm('aprobar-n2')}
          disabled={!accN2.habilitada || isPending}
          title={accN2.motivoDeshabilitada}
          data-action="aprobar-n2"
        >
          <CheckCircle2 className="mr-1 h-3.5 w-3.5" />
          Aprobar Nivel 2
        </Button>
      )}
      {accRechazar.visible && (
        <Button
          size="sm"
          variant="outline"
          onClick={abrirRechazar}
          disabled={!accRechazar.habilitada || isPending}
          title={accRechazar.motivoDeshabilitada}
          data-action="rechazar"
          className="text-rose-700 hover:bg-rose-50 hover:text-rose-800"
        >
          <XCircle className="mr-1 h-3.5 w-3.5" />
          Rechazar
        </Button>
      )}
      {accCancelar1.visible && (
        <Button
          size="sm"
          variant="outline"
          onClick={abrirCancelar1}
          disabled={!accCancelar1.habilitada || isPending}
          title={accCancelar1.motivoDeshabilitada}
          data-action="cancelar-1-firma"
          className="text-rose-700 hover:bg-rose-50 hover:text-rose-800"
        >
          <Ban className="mr-1 h-3.5 w-3.5" />
          Cancelar OC
        </Button>
      )}
      {accCancelarDoble.visible && (
        <Button
          size="sm"
          variant="outline"
          onClick={abrirCancelarDoble}
          disabled={!accCancelarDoble.habilitada || isPending}
          title={accCancelarDoble.motivoDeshabilitada}
          data-action="cancelar-doble-firma"
          className="text-rose-700 hover:bg-rose-50 hover:text-rose-800"
        >
          <Ban className="mr-1 h-3.5 w-3.5" />
          Cancelar (doble firma)
        </Button>
      )}
      {accDuplicar.visible && (
        <Button
          size="sm"
          variant="outline"
          onClick={abrirDuplicar}
          disabled={!accDuplicar.habilitada || isPending}
          title={accDuplicar.motivoDeshabilitada}
          data-action="duplicar-oc"
        >
          <Copy className="mr-1 h-3.5 w-3.5" />
          Duplicar OC
        </Button>
      )}

      {/* Confirm dialog (transmitir / aprobar-n1 / aprobar-n2). `key`
          forza remount para regenerar Idempotency-Key entre intentos
          (mismo patrón UF2-PR3-c en LineaDialog). */}
      <ConfirmDialog
        key={`confirm-${confirmInstance}`}
        action={confirmAction}
        oc={oc}
        isPending={isPending}
        conflictDialog={conflictDialog}
        queryClient={queryClient}
        onClose={() => setConfirmAction(null)}
        onConfirmTransmitir={async () => {
          await transmitirMut.mutateAsync({
            ordenCompraId: oc.id,
            idempotencyKey: crypto.randomUUID(),
          });
        }}
        onConfirmAprobarN1={async () => {
          await autorizarMut.mutateAsync({
            ordenCompraId: oc.id,
            command: { nivel: NivelAutorizacion.Nivel1, notas: null },
            idempotencyKey: crypto.randomUUID(),
          });
        }}
        onConfirmAprobarN2={async () => {
          await autorizarMut.mutateAsync({
            ordenCompraId: oc.id,
            command: { nivel: NivelAutorizacion.Nivel2, notas: null },
            idempotencyKey: crypto.randomUUID(),
          });
        }}
      />

      {/* Modal de motivo para rechazar */}
      <ModalMotivoOC
        key={`rechazar-${rechazarInstance}`}
        open={rechazarOpen}
        onOpenChange={setRechazarOpen}
        titulo={`Rechazar OC ${oc.folio}`}
        descripcion={
          oc.estado === /* EnAutorizacionJefeCompras */ 2
            ? 'Esta OC volverá a Rechazada y el comprador podrá editar y re-transmitir. Selecciona un motivo del catálogo.'
            : 'Esta OC volverá a Rechazada (vuelve al inbox del comprador). Selecciona un motivo del catálogo.'
        }
        aplicaA={MotivoRechazoAplicaA.OrdenCompra as MotivoRechazoAplicaA}
        ctaLabel="Rechazar"
        isPending={rechazarMut.isPending}
        onSubmit={async (values: ModalMotivoValues) => {
          try {
            await rechazarMut.mutateAsync({
              ordenCompraId: oc.id,
              command: {
                motivoRechazoId: values.motivoId,
                motivoRechazoTexto: values.motivoTexto,
                notas: null,
              },
              idempotencyKey: crypto.randomUUID(),
            });
            toast.success(`OC ${oc.folio} rechazada.`);
            setRechazarOpen(false);
          } catch (err) {
            if (
              handleOcMutationError(err, {
                ordenCompraId: oc.id,
                conflictDialog,
                queryClient,
              })
            ) {
              setRechazarOpen(false);
              return;
            }
            const msg = esApiError(err)
              ? err.problem.title
              : err instanceof Error
                ? err.message
                : 'Error desconocido.';
            toast.error('No se pudo rechazar la OC.', { description: msg });
          }
        }}
      />

      {/* Modal de motivo para Cancelar (1 firma) */}
      <ModalMotivoOC
        key={`cancelar1-${cancelar1Instance}`}
        open={cancelar1Open}
        onOpenChange={setCancelar1Open}
        titulo={`Cancelar OC ${oc.folio}`}
        descripcion="Esta OC pasará a estado Cancelada. Si está autorizada sin recepciones, las RQs de origen se liberan completas. Selecciona un motivo del catálogo."
        aplicaA={MotivoRechazoAplicaA.Cancelacion as MotivoRechazoAplicaA}
        ctaLabel="Cancelar OC"
        isPending={cancelarMut.isPending}
        onSubmit={async (values: ModalMotivoValues) => {
          try {
            await cancelarMut.mutateAsync({
              ordenCompraId: oc.id,
              command: {
                motivoCancelacionId: values.motivoId,
                motivoCancelacionTexto: values.motivoTexto,
              },
              idempotencyKey: crypto.randomUUID(),
            });
            toast.success(`OC ${oc.folio} cancelada.`);
            setCancelar1Open(false);
          } catch (err) {
            if (
              handleOcMutationError(err, {
                ordenCompraId: oc.id,
                conflictDialog,
                queryClient,
              })
            ) {
              setCancelar1Open(false);
              return;
            }
            const msg = esApiError(err)
              ? err.problem.title
              : err instanceof Error
                ? err.message
                : 'Error desconocido.';
            toast.error('No se pudo cancelar la OC.', { description: msg });
          }
        }}
      />

      {/* DobleFirmaDialog para Cancelar con recepciones parciales */}
      <DobleFirmaDialog
        key={`cancelar-doble-${cancelarDobleInstance}`}
        open={cancelarDobleOpen}
        onOpenChange={setCancelarDobleOpen}
        ocFolio={oc.folio}
        isPending={cancelarDobleMut.isPending}
        onSubmit={async (values: CancelarOcConRecepcionesValues) => {
          try {
            await cancelarDobleMut.mutateAsync({
              ordenCompraId: oc.id,
              command: values,
              idempotencyKey: crypto.randomUUID(),
            });
            toast.success(`OC ${oc.folio} cancelada (doble firma).`);
            setCancelarDobleOpen(false);
          } catch (err) {
            if (
              handleOcMutationError(err, {
                ordenCompraId: oc.id,
                conflictDialog,
                queryClient,
              })
            ) {
              setCancelarDobleOpen(false);
              return;
            }
            const msg = esApiError(err)
              ? err.problem.title
              : err instanceof Error
                ? err.message
                : 'Error desconocido.';
            toast.error('No se pudo cancelar la OC.', { description: msg });
          }
        }}
      />

      {/* ConfirmDuplicarDialog para Duplicar OC (Cancelada/Rechazada) */}
      <ConfirmDuplicarDialog
        key={`duplicar-${duplicarInstance}`}
        open={duplicarOpen}
        onOpenChange={setDuplicarOpen}
        ocFolio={oc.folio}
        ocEstadoLabel={estadoOcToString(oc.estado)}
        isPending={duplicarMut.isPending}
        onConfirm={async () => {
          try {
            // Extrae el código de sucursal del folio origen. Patrón
            // backend: `OC-{SUCURSAL_CODIGO}{FOLIO_ANIO}-{SECUENCIAL}`
            // donde SUCURSAL_CODIGO es 2-4 letras MAYÚSCULAS y
            // FOLIO_ANIO son 4 dígitos. Ej: `OC-MID2026-000003` →
            // segundo segmento `MID2026` → letras iniciales `MID`.
            const segundoSegmento = oc.folio.split('-')[1] ?? '';
            const matchSucursal = segundoSegmento.match(/^([A-Z]{2,4})/);
            const sucursalCodigo = matchSucursal?.[1] ?? '';

            const resp = await duplicarMut.mutateAsync({
              ordenCompraOrigenId: oc.id,
              command: {
                sucursalCodigo,
                folioAnio: new Date().getFullYear(),
                fechaDocumento: hoyLocalISO(),
              },
              idempotencyKey: crypto.randomUUID(),
            });
            toast.success(`OC duplicada: ${resp.folioNuevo}`, {
              description: `Origen: ${resp.folioOrigen}.`,
            });
            setDuplicarOpen(false);
            // Redirigir al detalle de la nueva OC.
            navigate({
              to: '/compras/ordenes/$id',
              params: { id: resp.ordenCompraNuevaId },
            });
          } catch (err) {
            if (
              handleOcMutationError(err, {
                ordenCompraId: oc.id,
                conflictDialog,
                queryClient,
              })
            ) {
              setDuplicarOpen(false);
              return;
            }
            const msg = esApiError(err)
              ? err.problem.title
              : err instanceof Error
                ? err.message
                : 'Error desconocido.';
            toast.error('No se pudo duplicar la OC.', { description: msg });
          }
        }}
      />
    </div>
  );
}

// ─── Confirm dialog interno ───────────────────────────────────────

interface ConfirmDialogProps {
  action: ConfirmAction;
  oc: OrdenCompraDetalleResponse;
  isPending: boolean;
  conflictDialog: ReturnType<typeof useConflictDialog>;
  queryClient: ReturnType<typeof useQueryClient>;
  onClose: () => void;
  onConfirmTransmitir: () => Promise<void>;
  onConfirmAprobarN1: () => Promise<void>;
  onConfirmAprobarN2: () => Promise<void>;
}

function ConfirmDialog({
  action,
  oc,
  isPending,
  conflictDialog,
  queryClient,
  onClose,
  onConfirmTransmitir,
  onConfirmAprobarN1,
  onConfirmAprobarN2,
}: ConfirmDialogProps) {
  // Inutilizado por re-mount via key prop pero declarado para que el
  // hook quede tracked en el stack (Idempotency-Key estable por mount).
  useFormIdempotencyKey();

  const open = action != null;
  const cfg = action ? getConfig(action, oc.folio) : null;

  async function handleConfirm() {
    if (action == null) return;
    try {
      if (action === 'transmitir') {
        await onConfirmTransmitir();
        toast.success(`OC ${oc.folio} transmitida a autorización.`);
      } else if (action === 'aprobar-n1') {
        await onConfirmAprobarN1();
        toast.success(`OC ${oc.folio} aprobada Nivel 1.`);
      } else if (action === 'aprobar-n2') {
        await onConfirmAprobarN2();
        toast.success(`OC ${oc.folio} aprobada Nivel 2.`);
      }
      onClose();
    } catch (err) {
      // 409 → ConflictDialog en modo simple (refresh + revisar). Cierra
      // el confirm para que el dialog se vea sin overlap visual.
      if (
        handleOcMutationError(err, {
          ordenCompraId: oc.id,
          conflictDialog,
          queryClient,
        })
      ) {
        onClose();
        return;
      }
      const msg = esApiError(err)
        ? err.problem.title
        : err instanceof Error
          ? err.message
          : 'Error desconocido.';
      toast.error('La acción falló.', { description: msg });
    }
  }

  return (
    <AlertDialog
      open={open}
      onOpenChange={(o) => {
        if (!o && !isPending) onClose();
      }}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{cfg?.titulo ?? ''}</AlertDialogTitle>
          <AlertDialogDescription>{cfg?.descripcion ?? ''}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>Cancelar</AlertDialogCancel>
          <AlertDialogAction
            onClick={handleConfirm}
            disabled={isPending}
            data-action="confirmar"
          >
            {isPending ? (
              <>
                <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" />
                Procesando…
              </>
            ) : (
              (cfg?.cta ?? 'Confirmar')
            )}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

function getConfig(action: Exclude<ConfirmAction, null>, folio: string) {
  if (action === 'transmitir') {
    return {
      titulo: `¿Transmitir OC ${folio} a autorización?`,
      descripcion:
        'La OC entrará al inbox de Nivel 1 (Jefe Compras). Los campos quedan congelados; cualquier ajuste posterior requiere rechazo + re-transmisión.',
      cta: 'Transmitir',
    };
  }
  if (action === 'aprobar-n1') {
    return {
      titulo: `¿Aprobar OC ${folio} (Nivel 1)?`,
      descripcion:
        'La OC pasará al inbox de Nivel 2 (Dirección). Tu firma queda registrada en el histórico de autorización.',
      cta: 'Aprobar Nivel 1',
    };
  }
  // aprobar-n2
  return {
    titulo: `¿Aprobar OC ${folio} (Nivel 2)?`,
    descripcion:
      'La OC pasará a Autorizada — se genera el PDF, se setea la FechaContabilizacion y se notifica al proveedor. No se puede revertir sin Cancelar (1 firma o doble firma).',
    cta: 'Aprobar Nivel 2',
  };
}
