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
import { Label } from '@/components/ui/label';
import { TextAreaField } from '@/components/erp/forms/TextAreaField';

/**
 * <c>&lt;BloquearLineaDialog/&gt;</c> — confirmación de bloqueo de línea
 * de crédito con motivo obligatorio (invariante del backend
 * <c>LC_MOTIVO_BLOQUEO_VACIO</c>). Variante simplificada del patrón
 * <c>ModalMotivo</c> de Compras: aquí el motivo es texto libre, sin
 * catálogo. El caller maneja la mutation vía <c>onConfirm(motivo)</c>.
 */
export interface BloquearLineaDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Descripción de la línea para el título (p.ej. "ACME · MXN"). */
  etiquetaLinea: string;
  onConfirm: (motivo: string) => void;
  isPending?: boolean;
}

export function BloquearLineaDialog({
  open,
  onOpenChange,
  etiquetaLinea,
  onConfirm,
  isPending,
}: BloquearLineaDialogProps) {
  const [motivo, setMotivo] = useState('');
  const motivoValido = motivo.trim().length > 0;

  function handleOpenChange(next: boolean) {
    if (!next) setMotivo('');
    onOpenChange(next);
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Bloquear línea de crédito</DialogTitle>
          <DialogDescription>
            {etiquetaLinea}: al bloquearla, la liberación de pedidos por
            crédito quedará retenida para esta línea hasta desbloquearla.
            El motivo es obligatorio y quedará visible en el detalle.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-1">
          <Label htmlFor="motivo-bloqueo" className="text-xs">
            Motivo del bloqueo (requerido)
          </Label>
          <TextAreaField
            value={motivo}
            onChange={(v) => setMotivo(v ?? '')}
            maxLength={400}
            textareaProps={{
              id: 'motivo-bloqueo',
              placeholder:
                'p. ej. Cartera vencida +90 días; baja de cobertura SOLUNION…',
            }}
          />
        </div>

        <DialogFooter>
          <Button
            type="button"
            variant="ghost"
            onClick={() => handleOpenChange(false)}
            disabled={isPending}
          >
            Cancelar
          </Button>
          <Button
            type="button"
            variant="destructive"
            disabled={!motivoValido || isPending}
            onClick={() => onConfirm(motivo.trim())}
          >
            {isPending ? 'Bloqueando…' : 'Bloquear línea'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
