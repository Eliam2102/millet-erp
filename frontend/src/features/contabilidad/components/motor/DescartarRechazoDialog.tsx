import { useState } from 'react';
import { toast } from 'sonner';
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
import { useBodyScopedIdempotencyKey } from '@/lib/api';
import { useDescartarRechazo } from '../../api/motor';
import type { RechazoContable } from '../../api/motor-types';
import { MOTIVO_DESCARTE_MAXIMO, MOTIVO_DESCARTE_MINIMO, mensajeMotor, textoCausa } from '../../lib/motor';

interface Props {
  rechazo: RechazoContable | null;
  onClose: () => void;
}

/** Descartar un rechazo (C1.4): el evento no generará póliza. Pide motivo de al menos 10 caracteres. */
export function DescartarRechazoDialog({ rechazo, onClose }: Props) {
  const descartar = useDescartarRechazo();
  const keyFor = useBodyScopedIdempotencyKey();
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState('');

  const largo = motivo.trim().length;
  const deshabilitado =
    largo < MOTIVO_DESCARTE_MINIMO ? `El motivo lleva al menos ${MOTIVO_DESCARTE_MINIMO} caracteres` : undefined;

  function cerrar() {
    if (descartar.isPending) return;
    setMotivo('');
    setError('');
    onClose();
  }

  function confirmar() {
    if (!rechazo || deshabilitado) return;
    setError('');
    const body = { id: rechazo.id, motivo: motivo.trim() };
    descartar.mutate(
      { ...body, idempotencyKey: keyFor(body) },
      {
        onSuccess: () => {
          toast.success('Rechazo descartado. El evento no generará póliza.');
          setMotivo('');
          onClose();
        },
        onError: (e) => setError(mensajeMotor(e)),
      },
    );
  }

  return (
    <Dialog open={!!rechazo} onOpenChange={(o) => !o && cerrar()}>
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>Descartar rechazo</DialogTitle>
          <DialogDescription>
            El evento <span className="font-mono">{rechazo?.tipoEvento}</span> ({textoCausa(rechazo?.causa)}) no
            generará póliza. Queda registrado quién lo descartó y por qué.
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="descartar-motivo">
              Motivo<span aria-hidden="true"> *</span>
            </Label>
            <Textarea
              id="descartar-motivo"
              rows={3}
              className="resize-none"
              maxLength={MOTIVO_DESCARTE_MAXIMO}
              value={motivo}
              aria-describedby="descartar-motivo-ayuda"
              onChange={(e) => setMotivo(e.target.value)}
            />
            <span id="descartar-motivo-ayuda" className="text-xs text-ink-muted">
              <span className="tabular-nums">
                {largo}/{MOTIVO_DESCARTE_MAXIMO}
              </span>{' '}
              · mínimo {MOTIVO_DESCARTE_MINIMO} caracteres.
            </span>
          </div>
          {error && (
            <p role="alert" className="rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">
              {error}
            </p>
          )}
        </DialogBody>
        <DialogFooter>
          <Button variant="ghost" onClick={cerrar} disabled={descartar.isPending}>
            Cancelar
          </Button>
          <Button
            variant="secondary-danger"
            onClick={confirmar}
            disabled={!!deshabilitado || descartar.isPending}
            title={deshabilitado}
          >
            {descartar.isPending ? 'Descartando…' : 'Descartar rechazo'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
