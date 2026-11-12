import { useState } from 'react';
import { FormGrid, FormCampo } from '@/components/erp';
import {
  Dialog,
  DialogContent,
  DialogBody,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { TextAreaField } from '@/components/erp/forms/TextAreaField';

/**
 * M-237 (CXC-CARTERA/corregir): un solo diálogo para las tres decisiones del estado de cuenta — corregir el plazo de una factura,
 * solicitar la reversa de un movimiento mal aplicado y rechazar una solicitud. El motivo siempre es obligatorio y queda en el rastro.
 */
export type ModoCorreccion = 'plazo' | 'reversa' | 'rechazar';

export interface CorreccionCarteraDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  modo: ModoCorreccion;
  /** Texto de contexto: «Factura FV-1» o «Pago de 4,000.00 sobre FV-1». */
  objetivo: string;
  /** Solo en modo plazo. */
  plazoActualDias?: number;
  isPending?: boolean;
  onConfirm: (datos: { motivo: string; plazoDias?: number }) => void;
}

const TEXTOS: Record<ModoCorreccion, { titulo: string; descripcion: string; accion: string; pendiente: string }> = {
  plazo: {
    titulo: 'Corregir plazo',
    descripcion:
      'El vencimiento se recalcula desde la fecha de timbrado y la antigüedad, las alertas y el bloqueo por vencimiento usan el nuevo plazo. Queda registrado quién lo cambió, por qué y el antes/después.',
    accion: 'Corregir plazo',
    pendiente: 'Corrigiendo…',
  },
  reversa: {
    titulo: 'Solicitar reversa de movimiento',
    descripcion:
      'La solicitud queda pendiente: la cartera no cambia hasta que otra persona con permiso la autorice.',
    accion: 'Solicitar reversa',
    pendiente: 'Solicitando…',
  },
  rechazar: {
    titulo: 'Rechazar reversa',
    descripcion: 'La cartera no cambia. El motivo del rechazo es obligatorio y queda registrado.',
    accion: 'Rechazar',
    pendiente: 'Rechazando…',
  },
};

export function CorreccionCarteraDialog({
  open,
  onOpenChange,
  modo,
  objetivo,
  plazoActualDias,
  isPending,
  onConfirm,
}: CorreccionCarteraDialogProps) {
  const t = TEXTOS[modo];
  const [motivo, setMotivo] = useState('');
  const [plazo, setPlazo] = useState(plazoActualDias != null ? String(plazoActualDias) : '');
  const plazoNum = Number(plazo);
  const plazoValido =
    modo !== 'plazo' ||
    (plazo.trim() !== '' && Number.isInteger(plazoNum) && plazoNum >= 0 && plazoNum <= 365 && plazoNum !== plazoActualDias);
  const valido = motivo.trim().length > 0 && plazoValido;

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
          <p className="mb-3 text-sm font-medium text-ink">{objetivo}</p>
          {modo === 'reversa' && (
            <Alert className="mb-3">
              <AlertDescription>
                Al autorizarse, el movimiento deja de contar en la cartera pero Facturación lo sigue teniendo vigente: la conciliación
                de cartera contra Facturación mostrará la diferencia «cancelado solo en cartera» y, si fue un cobro de mostrador, su
                póliza seguirá registrada. Es lo esperado; no se oculta.
              </AlertDescription>
            </Alert>
          )}
          <FormGrid>
            {modo === 'plazo' && (
              <FormCampo span={12} className="space-y-1">
                <Label htmlFor="correccion-plazo" className="text-xs">
                  Plazo nuevo en días (actual: {plazoActualDias}) *
                </Label>
                <Input
                  id="correccion-plazo"
                  inputMode="numeric"
                  value={plazo}
                  onChange={(e) => setPlazo(e.target.value.replace(/\D/g, ''))}
                />
              </FormCampo>
            )}
            <FormCampo span={12} className="space-y-1">
              <Label htmlFor="correccion-motivo" className="text-xs">
                Motivo *
              </Label>
              <TextAreaField
                value={motivo}
                onChange={(v) => setMotivo(v ?? '')}
                maxLength={400}
                textareaProps={{ id: 'correccion-motivo', placeholder: 'p. ej. Se pactó otro plazo con el cliente' }}
              />
            </FormCampo>
          </FormGrid>
        </DialogBody>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={() => handleOpenChange(false)} disabled={isPending}>
            Cancelar
          </Button>
          <Button
            type="button"
            title={!valido ? 'Completa el motivo y los datos para continuar.' : undefined}
            disabled={!valido || isPending}
            onClick={() => onConfirm({ motivo: motivo.trim(), plazoDias: modo === 'plazo' ? plazoNum : undefined })}
          >
            {isPending ? t.pendiente : t.accion}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
