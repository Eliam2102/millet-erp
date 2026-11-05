import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/textarea';
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { esApiError } from '@/lib/api';

interface Props {
  /** `null` = cerrado. */
  modo: 'desactivar' | 'reactivar' | null;
  /** «proveedor P-4411», «cliente C-0087»… */
  etiqueta: string;
  pending: boolean;
  /** Error del servidor (p. ej. lo que sigue abierto); se muestra dentro del diálogo. */
  error: Error | null;
  onConfirm: (motivo: string) => void;
  onCancel: () => void;
}

/**
 * Inactivar o reactivar un maestro (proveedor, cliente, artículo): pide el motivo (5 a 500 caracteres)
 * y muestra aquí mismo lo que el servidor rechaza, por ejemplo las OC o el saldo que siguen abiertos.
 */
export function CambioEstatusMaestroDialog({ modo, etiqueta, pending, error, onConfirm, onCancel }: Props) {
  const [motivo, setMotivo] = useState('');
  const trim = motivo.trim();
  const valido = trim.length >= 5 && trim.length <= 500;
  const inactivar = modo === 'desactivar';
  const mensaje = error == null ? null : esApiError(error) ? (error.problem.detail ?? error.problem.title) : error.message;

  return (
    <Dialog
      open={modo != null}
      onOpenChange={(abierto) => {
        if (!abierto) {
          setMotivo('');
          onCancel();
        }
      }}
    >
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>{inactivar ? 'Inactivar' : 'Reactivar'} {etiqueta}</DialogTitle>
          <DialogDescription>
            {inactivar
              ? 'Antes de inactivar reviso lo que tenga abierto. Si hay algo pendiente, no se inactiva.'
              : 'Queda activo otra vez para nuevas operaciones.'}{' '}
            El motivo queda en la historia del registro.
          </DialogDescription>
        </DialogHeader>
        <DialogBody>
          <div className="space-y-2 py-2">
            <label htmlFor="motivo-estatus" className="text-xs font-medium text-ink">
              Motivo (obligatorio, 5 a 500 caracteres):
            </label>
            <Textarea
              id="motivo-estatus"
              value={motivo}
              onChange={(e) => setMotivo(e.target.value)}
              rows={3}
              maxLength={500}
              className="resize-none"
            />
            <div className="flex items-center justify-between text-xs text-ink-muted">
              <span>
                {trim.length > 0 && trim.length < 5 && (
                  <span className="text-danger-fg">Mínimo 5 caracteres</span>
                )}
              </span>
              <span>{motivo.length} / 500</span>
            </div>
            {mensaje != null && (
              <p role="alert" className="rounded-md border border-danger/40 bg-danger-bg/10 p-2 text-sm text-danger-fg">
                {mensaje}
              </p>
            )}
          </div>
        </DialogBody>
        <DialogFooter>
          <Button type="button" variant="outline" disabled={pending} onClick={onCancel}>
            Cancelar
          </Button>
          <Button
            type="button"
            variant={inactivar ? 'destructive' : 'default'}
            disabled={!valido || pending}
            onClick={() => onConfirm(trim)}
          >
            {pending ? 'Guardando…' : inactivar ? 'Inactivar' : 'Reactivar'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
