import { FormGrid, FormCampo } from '@/components/erp';
import { useState } from 'react';
import {
  Dialog,
  DialogContent,
  DialogBody,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { TextAreaField } from '@/components/erp/forms/TextAreaField';

/**
 * <c>&lt;DesbloquearLineaDialog/&gt;</c> — desbloqueo manual autorizado de
 * una línea de crédito con motivo obligatorio (C7.5, invariante del
 * backend <c>LC_MOTIVO_DESBLOQUEO_VACIO</c>). Si el bloqueo fue automático
 * por vencimiento, el desbloqueo cubre lo vencido a la fecha: solo otra
 * factura que rebase plazo + tolerancia vuelve a bloquear.
 */
export interface DesbloquearLineaDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Descripción de la línea para el título (p.ej. "ACME · MXN"). */
  etiquetaLinea: string;
  onConfirm: (motivo: string) => void;
  isPending?: boolean;
  /** La línea está bloqueada por vencimiento (bloqueo automático). */
  bloqueoAutomatico?: boolean;
}

export function DesbloquearLineaDialog({
  open,
  onOpenChange,
  etiquetaLinea,
  onConfirm,
  isPending,
  bloqueoAutomatico,
}: DesbloquearLineaDialogProps) {
  const [motivo, setMotivo] = useState('');
  const motivoValido = motivo.trim().length > 0;

  function handleOpenChange(next: boolean) {
    if (!next) setMotivo('');
    onOpenChange(next);
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>Desbloquear línea de crédito</DialogTitle>
          <DialogDescription>
            {etiquetaLinea}:{' '}
            {bloqueoAutomatico
              ? 'la línea se bloqueó por facturas vencidas más allá del plazo y la tolerancia. Al desbloquearla autorizas lo vencido a hoy; si otra factura rebasa el plazo después, se volverá a bloquear.'
              : 'la liberación de pedidos por crédito volverá a evaluarse para esta línea.'}{' '}
            El motivo es obligatorio y queda registrado.
          </DialogDescription>
        </DialogHeader>
        <DialogBody>
          <FormGrid>
            <FormCampo span={12} className="space-y-1">
              <Label htmlFor="motivo-desbloqueo" className="text-xs">
                Motivo del desbloqueo *
              </Label>
              <TextAreaField
                value={motivo}
                onChange={(v) => setMotivo(v ?? '')}
                maxLength={400}
                textareaProps={{
                  id: 'motivo-desbloqueo',
                  placeholder: 'p. ej. Acuerdo de pago autorizado por Crédito y Cobranza…',
                }}
              />
            </FormCampo>
          </FormGrid>
        </DialogBody>
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
            disabled={!motivoValido || isPending}
            onClick={() => onConfirm(motivo.trim())}
          >
            {isPending ? 'Desbloqueando…' : 'Desbloquear línea'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
