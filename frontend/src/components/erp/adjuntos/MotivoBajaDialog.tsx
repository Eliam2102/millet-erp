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

export const MOTIVO_BAJA_MIN = 5;
export const MOTIVO_BAJA_MAX = 500;

interface MotivoBajaDialogProps {
  nombreArchivo: string;
  /** Lanza para mostrar el error en el diálogo (se queda abierto). */
  onConfirmar: (motivo: string) => Promise<void>;
  onCerrar: () => void;
  /** Mensaje de error ya traducido por el caller. */
  error: string | null;
}

/**
 * Diálogo de baja lógica con motivo obligatorio (5-500). La baja es
 * irreversible y el archivo físico se conserva (contrato G1.2). Radix
 * resuelve foco atrapado, Esc y devolución del foco.
 */
export function MotivoBajaDialog({
  nombreArchivo,
  onConfirmar,
  onCerrar,
  error,
}: MotivoBajaDialogProps) {
  const id = useId();
  const [motivo, setMotivo] = useState('');
  const [enviando, setEnviando] = useState(false);
  const largo = motivo.trim().length;
  const valido = largo >= MOTIVO_BAJA_MIN && largo <= MOTIVO_BAJA_MAX;

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
      <DialogContent closeLabel="Cerrar" data-component="motivo-baja-dialog">
        <DialogHeader>
          <DialogTitle>Dar de baja el documento</DialogTitle>
          <DialogDescription>
            {nombreArchivo}. La baja es irreversible; el archivo se conserva
            para auditoría pero deja de estar disponible para descarga.
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-1.5">
          <Label htmlFor={id} className="text-xs font-medium text-ink-strong">
            Motivo de la baja
          </Label>
          <Textarea
            id={id}
            rows={3}
            value={motivo}
            maxLength={MOTIVO_BAJA_MAX}
            onChange={(e) => setMotivo(e.target.value)}
            aria-describedby={`${id}-ayuda`}
            aria-invalid={largo > 0 && !valido}
            disabled={enviando}
          />
          <p id={`${id}-ayuda`} className="text-xs text-ink-muted">
            Entre {MOTIVO_BAJA_MIN} y {MOTIVO_BAJA_MAX} caracteres ({largo}).
          </p>
        </div>
        {error && (
          <p role="alert" className="text-xs text-danger-fg">
            {error}
          </p>
        )}
        <DialogFooter>
          <Button variant="ghost" onClick={onCerrar} disabled={enviando}>
            Cancelar
          </Button>
          <Button
            variant="secondary-danger"
            onClick={confirmar}
            disabled={!valido || enviando}
            title={
              valido
                ? undefined
                : `Escribe un motivo de al menos ${MOTIVO_BAJA_MIN} caracteres.`
            }
            data-action="confirmar-baja"
          >
            {enviando && <Loader2 className="animate-spin" />}
            Dar de baja
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
