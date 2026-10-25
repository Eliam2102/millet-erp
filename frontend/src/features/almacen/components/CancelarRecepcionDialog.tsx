import { useId, useState } from 'react';
import { Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { formatCantidad } from '@/lib/format-cantidad';
import { useCancelarRecepcion } from '@/features/almacen/api/useRecepciones';
import {
  MOTIVOS_CANCELACION_RECEPCION,
  type CancelarRecepcionResponse,
  type MotivoCancelacionRecepcion,
  type RecepcionDetalle,
} from '@/features/almacen/api/types';

export const MOTIVO_TEXTO_MAX = 450;

export interface CancelarRecepcionDialogProps {
  recepcion: RecepcionDetalle;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onCancelada: (respuesta: CancelarRecepcionResponse) => void;
}

/**
 * Cancela con motivo una recepción mal capturada (86e3tabdp). Muestra el
 * efecto antes de confirmar; el rechazo del servidor (422) se pinta tal cual.
 */
export function CancelarRecepcionDialog({
  recepcion,
  open,
  onOpenChange,
  onCancelada,
}: CancelarRecepcionDialogProps) {
  const ids = useId();
  const [codigo, setCodigo] = useState<MotivoCancelacionRecepcion | ''>('');
  const [texto, setTexto] = useState('');
  const [error, setError] = useState<string | null>(null);
  const cancelar = useCancelarRecepcion(recepcion.id);
  const keyFor = useBodyScopedIdempotencyKey();
  const largo = texto.trim().length;
  const valido = codigo !== '' && largo > 0 && largo <= MOTIVO_TEXTO_MAX;
  const enviando = cancelar.isPending;

  function confirmar() {
    if (!valido || enviando) return;
    const command = { motivoCodigo: codigo, motivoTexto: texto.trim() };
    setError(null);
    cancelar.mutate(
      { command, idempotencyKey: keyFor(command) },
      {
        onSuccess: (r) => {
          onOpenChange(false);
          onCancelada(r);
        },
        onError: (e) =>
          setError(
            esApiError(e)
              ? (e.problem.detail ?? e.problem.title)
              : 'No se pudo cancelar la recepción. Intenta de nuevo.',
          ),
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={(v) => !enviando && onOpenChange(v)}>
      <DialogContent size="sm" closeLabel="Cerrar" data-component="cancelar-recepcion-dialog">
        <DialogHeader>
          <DialogTitle>Cancelar recepción {recepcion.folio}</DialogTitle>
          <DialogDescription>
            Úsalo cuando la recepción se capturó mal. Esta acción no se puede deshacer; después podrás capturar la correcta.
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-4">
          <section aria-labelledby={`${ids}-efectos`} className="space-y-1.5">
            <h3 id={`${ids}-efectos`} className="text-xs font-semibold text-ink-strong">
              Qué pasará al confirmar
            </h3>
            <ul className="list-disc space-y-1 pl-5 text-sm text-ink">
              {recepcion.lineas.map((l) => (
                <li key={l.id}>
                  Existencia de{' '}
                  <span className="font-medium">
                    {l.articuloClave ?? l.articuloDescripcion ?? l.articuloId}
                  </span>
                  : <span className="tabular-nums">−{formatCantidad(l.cantidad)}</span>{' '}
                  {l.unidadMedida}
                  {l.ubicacionClave && (
                    <>
                      {' '}
                      en <span className="font-mono text-xs">{l.ubicacionClave}</span>
                    </>
                  )}
                </li>
              ))}
              {recepcion.ordenCompraId && (
                <li>
                  Lo recibido regresa a pendiente en la OC{' '}
                  <span className="font-mono text-xs">
                    {recepcion.ordenCompraFolio ?? recepcion.ordenCompraId}
                  </span>
                  .
                </li>
              )}
              {recepcion.cfdiRecibidoId && (
                <li>
                  El CFDI{' '}
                  <span className="font-mono text-xs">
                    {recepcion.cfdiUuidFiscal ?? `${recepcion.cfdiRecibidoId.slice(0, 8)}…`}
                  </span>{' '}
                  queda libre para la recepción correcta.
                </li>
              )}
              {recepcion.facturaId && (
                <li>
                  Se desvincula la factura{' '}
                  <span className="font-mono text-xs">
                    {recepcion.facturaFolio ?? `${recepcion.facturaId.slice(0, 8)}…`}
                  </span>
                  .
                </li>
              )}
              <li>Se genera un movimiento de cancelación por cada entrada.</li>
            </ul>
          </section>

          <div className="grid gap-1.5">
            <Label htmlFor={`${ids}-codigo`} className="text-xs font-medium text-ink-strong">
              Motivo *
            </Label>
            <Select
              value={codigo}
              onValueChange={(v) => setCodigo(v as MotivoCancelacionRecepcion)}
              disabled={enviando}
            >
              <SelectTrigger id={`${ids}-codigo`} aria-required="true">
                <SelectValue placeholder="Selecciona el motivo" />
              </SelectTrigger>
              <SelectContent>
                {MOTIVOS_CANCELACION_RECEPCION.map((m) => (
                  <SelectItem key={m.codigo} value={m.codigo}>
                    {m.etiqueta}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="grid gap-1.5">
            <Label htmlFor={`${ids}-texto`} className="text-xs font-medium text-ink-strong">
              Explica qué se capturó mal *
            </Label>
            <Textarea
              id={`${ids}-texto`}
              rows={3}
              value={texto}
              maxLength={MOTIVO_TEXTO_MAX}
              onChange={(e) => setTexto(e.target.value)}
              aria-required="true"
              aria-describedby={`${ids}-ayuda`}
              disabled={enviando}
            />
            <p id={`${ids}-ayuda`} className="text-xs text-ink-muted">
              {largo} de {MOTIVO_TEXTO_MAX} caracteres.
            </p>
          </div>

          {error && <AvisoEnLinea tono="error">{error}</AvisoEnLinea>}
        </DialogBody>
        <DialogFooter>
          <Button variant="ghost" onClick={() => onOpenChange(false)} disabled={enviando}>
            Volver
          </Button>
          <Button
            variant="secondary-danger"
            onClick={confirmar}
            disabled={!valido || enviando}
            data-action="confirmar-cancelacion"
          >
            {enviando && <Loader2 className="animate-spin" aria-hidden="true" />}
            Confirmar cancelación
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
