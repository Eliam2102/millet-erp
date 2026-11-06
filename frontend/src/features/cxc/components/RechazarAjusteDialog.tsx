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
 * <c>&lt;RechazarAjusteDialog/&gt;</c> — CXC-06: rechazar el ajuste de un cobro corto rechaza la propuesta y libera el saldo de
 * sus facturas. Motivo obligatorio (invariante del backend, validador de <c>RechazarAjustePropuestaCommand</c>).
 */
export interface RechazarAjusteDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onConfirm: (motivo: string) => void;
  isPending?: boolean;
}

export function RechazarAjusteDialog({ open, onOpenChange, onConfirm, isPending }: RechazarAjusteDialogProps) {
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
          <DialogTitle>Rechazar ajuste</DialogTitle>
          <DialogDescription>
            La propuesta quedará rechazada y el saldo de sus facturas se libera. Quien la propuso puede registrar otra
            por el depósito exacto o con el ajuste corregido. El motivo es obligatorio y queda registrado.
          </DialogDescription>
        </DialogHeader>
        <DialogBody>
          <FormGrid>
            <FormCampo span={12} className="space-y-1">
              <Label htmlFor="motivo-rechazo-ajuste" className="text-xs">
                Motivo del rechazo *
              </Label>
              <TextAreaField
                value={motivo}
                onChange={(v) => setMotivo(v ?? '')}
                maxLength={370}
                textareaProps={{
                  id: 'motivo-rechazo-ajuste',
                  placeholder: 'p. ej. La diferencia no es comisión: el cliente debe completar el pago',
                }}
              />
            </FormCampo>
          </FormGrid>
        </DialogBody>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={() => handleOpenChange(false)} disabled={isPending}>
            Cancelar
          </Button>
          <Button type="button" title={!motivoValido ? 'Escribe el motivo para continuar.' : isPending ? 'Espera a que termine la operación.' : undefined}
            disabled={!motivoValido || isPending} onClick={() => onConfirm(motivo.trim())}>
            {isPending ? 'Rechazando…' : 'Rechazar ajuste'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
