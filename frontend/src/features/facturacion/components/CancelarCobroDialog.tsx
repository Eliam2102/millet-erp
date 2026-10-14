import { useId, useState } from 'react';
import { Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';

export const MOTIVO_CANCELACION_MIN = 5;
/** Tope del backend (CancelarCobroMostradorValidator: MaximumLength(254)). */
export const MOTIVO_CANCELACION_MAX = 254;

interface CancelarCobroDialogProps {
  folio: string;
  total: string;
  /** Lanza para mostrar el error en el diálogo (se queda abierto). */
  onConfirmar: (motivo: string) => Promise<void>;
  onCerrar: () => void;
  /** Mensaje de error ya traducido por el caller. */
  error: string | null;
}

/**
 * Diálogo de cancelación de un cobro de caja con motivo obligatorio. El
 * cobro no se borra: se genera una reversa en la sesión abierta o un ajuste
 * pendiente para la siguiente apertura (`[Decisión 12-C]`). Mismo patrón que
 * <c>MotivoBajaDialog</c>; Radix resuelve foco atrapado, Esc y devolución del
 * foco.
 */
export function CancelarCobroDialog({
  folio,
  total,
  onConfirmar,
  onCerrar,
  error,
}: CancelarCobroDialogProps) {
  const id = useId();
  const [motivo, setMotivo] = useState('');
  const [enviando, setEnviando] = useState(false);
  const largo = motivo.trim().length;
  const valido = largo >= MOTIVO_CANCELACION_MIN && largo <= MOTIVO_CANCELACION_MAX;

  async function confirmar() {
    if (!valido || enviando) return;
    setEnviando(true);
    try {
      await onConfirmar(motivo.trim());
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Dialog open onOpenChange={(abierto) => !abierto && !enviando && onCerrar()}>
      <DialogContent closeLabel="Cerrar" data-component="cancelar-cobro-dialog">
        <DialogHeader>
          <DialogTitle>Cancelar el cobro</DialogTitle>
          <DialogDescription>
            {folio} · {total}. El cobro no se borra: queda «Cancelado» y la caja registra una
            reversa por el mismo importe. El comprobante vuelve a quedar por cobrar.
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-1.5">
          <Label htmlFor={id} className="text-xs font-medium text-ink-strong">
            Motivo de la cancelación
          </Label>
          <Textarea
            id={id}
            rows={3}
            value={motivo}
            maxLength={MOTIVO_CANCELACION_MAX}
            onChange={(e) => setMotivo(e.target.value)}
            aria-describedby={`${id}-ayuda`}
            aria-invalid={largo > 0 && !valido}
            disabled={enviando}
          />
          <p id={`${id}-ayuda`} className="text-xs text-ink-muted">
            Entre {MOTIVO_CANCELACION_MIN} y {MOTIVO_CANCELACION_MAX} caracteres ({largo}).
          </p>
        </div>
        {error && (
          <p role="alert" className="text-xs text-danger-fg">
            {error}
          </p>
        )}
        <DialogFooter>
          <Button variant="ghost" onClick={onCerrar} disabled={enviando}>
            Volver
          </Button>
          <Button
            variant="secondary-danger"
            onClick={confirmar}
            disabled={!valido || enviando}
            title={
              valido
                ? undefined
                : `Escribe un motivo de al menos ${MOTIVO_CANCELACION_MIN} caracteres.`
            }
            data-action="confirmar-cancelacion-cobro"
          >
            {enviando && <Loader2 className="animate-spin" />}
            Cancelar cobro
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
