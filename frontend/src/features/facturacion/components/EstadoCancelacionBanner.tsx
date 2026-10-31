import { Button } from '@/components/ui/button';
import {
  ESTADO_SOLICITUD_CANCELACION_LABEL,
  type ConsultarCancelacionResponse,
  type EstadoSolicitudCancelacion,
} from '@/features/facturacion/api/types';
import { useDecidirCancelacion } from '@/features/facturacion/api/useFacturas';
import { codigoSoporte, esApiError, useFormIdempotencyKey } from '@/lib/api';
import { toast } from 'sonner';

/**
 * <c>&lt;EstadoCancelacionBanner/&gt;</c> — estado de la última solicitud de
 * cancelación de un comprobante (F6.5): pendiente de autorización del Contador
 * General (con «Retirar» para quien puede solicitar), no autorizada (con su
 * motivo; la factura sigue vigente) o su avance ante el SAT.
 */
export function EstadoCancelacionBanner({
  comprobanteId,
  cancelacion,
  puedeRetirar,
}: {
  comprobanteId: string;
  cancelacion: ConsultarCancelacionResponse | undefined;
  puedeRetirar: boolean;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const decidir = useDecidirCancelacion();
  if (cancelacion?.estadoSolicitud == null) return null;

  const estado = cancelacion.estadoSolicitud as EstadoSolicitudCancelacion;
  const pendiente = estado === 'PendienteAutorizacion';
  const noAutorizada = estado === 'RechazadaPorAutorizador';

  function retirar() {
    decidir.mutate(
      { id: comprobanteId, accion: 'retirar', idempotencyKey },
      {
        onSuccess: () => toast.success('Solicitud de cancelación retirada.'),
        onError: (error) =>
          toast.error(esApiError(error) ? error.problem.title : 'No se pudo retirar la solicitud.', {
            description: esApiError(error)
              ? (error.problem.detail ?? codigoSoporte(error))
              : undefined,
          }),
      },
    );
  }

  return (
    <div
      className="flex flex-wrap items-start justify-between gap-2 rounded-md border border-line bg-warning-note-bg px-3 py-2 text-sm"
      data-print="hidden"
    >
      <div className="space-y-0.5">
        <p>
          <span className="font-medium">Solicitud de cancelación:</span>{' '}
          {ESTADO_SOLICITUD_CANCELACION_LABEL[estado] ?? estado}
          {cancelacion.motivoSat ? ` · motivo ${cancelacion.motivoSat}` : ''}
          {cancelacion.uuidSustituto ? ` · sustituye ${cancelacion.uuidSustituto}` : ''}
          {cancelacion.estatusSat ? ` · SAT ${cancelacion.estatusSat}` : ''}
          {cancelacion.mensajeError ? (
            <span className="text-danger-fg"> · {cancelacion.mensajeError}</span>
          ) : null}
        </p>
        {cancelacion.solicitadaPor && (
          <p className="text-xs text-ink-muted">Solicitó: {cancelacion.solicitadaPor}</p>
        )}
        {pendiente && (
          <p className="text-xs text-ink-muted">
            No se envía al SAT hasta que el Contador General la autorice.
          </p>
        )}
        {cancelacion.decididaPor && (
          <p className="text-xs text-ink-muted">
            {noAutorizada ? 'No la autorizó' : 'Autorizó'}: {cancelacion.decididaPor}
          </p>
        )}
        {noAutorizada && cancelacion.motivoRechazo && (
          <p className="text-danger-fg">
            Motivo: {cancelacion.motivoRechazo}. La factura sigue vigente.
          </p>
        )}
      </div>
      {pendiente && puedeRetirar && (
        <Button variant="outline" size="sm" onClick={retirar} disabled={decidir.isPending}>
          {decidir.isPending ? 'Retirando…' : 'Retirar solicitud'}
        </Button>
      )}
    </div>
  );
}
