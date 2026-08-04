import { useState } from 'react';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/textarea';

export interface MotivoDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  titulo: string;
  descripcion: string;
  confirmLabel: string;
  /** Callback con el motivo capturado; el caller cierra el dialog en success. */
  onConfirm: (motivo: string) => void;
  pending?: boolean;
}

/**
 * Dialog genérico de acción-con-motivo (TES-FE-PR2): revertir pago
 * (RN-10 exige motivo) y solicitar cancelación de pasivo. Patrón
 * <c>ModalMotivo</c> de Compras.
 */
export function MotivoDialog({
  open,
  onOpenChange,
  titulo,
  descripcion,
  confirmLabel,
  onConfirm,
  pending = false,
}: MotivoDialogProps) {
  const [motivo, setMotivo] = useState('');

  function cerrar(next: boolean) {
    if (!next) setMotivo('');
    onOpenChange(next);
  }

  return (
    <Dialog open={open} onOpenChange={cerrar}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{titulo}</DialogTitle>
          <DialogDescription>{descripcion}</DialogDescription>
        </DialogHeader>
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground" htmlFor="motivo">
            Motivo (obligatorio)
          </label>
          <Textarea
            id="motivo"
            value={motivo}
            onChange={(e) => setMotivo(e.target.value)}
            maxLength={400}
            rows={3}
            placeholder="Describe el motivo…"
          />
        </div>
        <DialogFooter>
          <Button variant="ghost" onClick={() => cerrar(false)} disabled={pending}>
            Cancelar
          </Button>
          <Button
            onClick={() => onConfirm(motivo.trim())}
            disabled={pending || motivo.trim().length === 0}
          >
            {pending ? 'Procesando…' : confirmLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
