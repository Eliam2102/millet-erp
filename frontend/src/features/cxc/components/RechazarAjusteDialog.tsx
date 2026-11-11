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
  /** M-240: el mismo diálogo de motivo sirve para revertir un ajuste ya autorizado. */
  modo?: 'rechazar' | 'revertir';
}

const TEXTOS = {
  rechazar: {
    titulo: 'Rechazar ajuste',
    descripcion:
      'La propuesta quedará rechazada y el saldo de sus facturas se libera. Quien la propuso puede registrar otra por el depósito exacto o con el ajuste corregido. El motivo es obligatorio y queda registrado.',
    etiqueta: 'Motivo del rechazo *',
    placeholder: 'p. ej. La diferencia no es comisión: el cliente debe completar el pago',
    accion: 'Rechazar ajuste',
    pendiente: 'Rechazando…',
    maximo: 370,
  },
  revertir: {
    titulo: 'Revertir ajuste',
    descripcion:
      'El faltante regresa al saldo de la factura y se registra la póliza contraria, ligada a la póliza del cobro. El motivo es obligatorio y queda registrado.',
    etiqueta: 'Motivo de la reversa *',
    placeholder: 'p. ej. El banco devolvió la comisión',
    accion: 'Revertir ajuste',
    pendiente: 'Revirtiendo…',
    maximo: 400,
  },
} as const;

export function RechazarAjusteDialog({ open, onOpenChange, onConfirm, isPending, modo = 'rechazar' }: RechazarAjusteDialogProps) {
  const t = TEXTOS[modo];
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
          <DialogTitle>{t.titulo}</DialogTitle>
          <DialogDescription>{t.descripcion}</DialogDescription>
        </DialogHeader>
        <DialogBody>
          <FormGrid>
            <FormCampo span={12} className="space-y-1">
              <Label htmlFor={`motivo-${modo}-ajuste`} className="text-xs">
                {t.etiqueta}
              </Label>
              <TextAreaField
                value={motivo}
                onChange={(v) => setMotivo(v ?? '')}
                maxLength={t.maximo}
                textareaProps={{
                  id: `motivo-${modo}-ajuste`,
                  placeholder: t.placeholder,
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
            {isPending ? t.pendiente : t.accion}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
