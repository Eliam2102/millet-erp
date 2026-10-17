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
  /** Textos para reusar el diálogo en otras acciones con motivo (default: baja de documento). */
  titulo?: string;
  descripcion?: string;
  etiqueta?: string;
  accion?: string;
  variante?: 'secondary-danger' | 'default';
  /** Mínimo de caracteres del motivo (default MOTIVO_BAJA_MIN). */
  minimo?: number;
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
  titulo = 'Dar de baja el documento',
  descripcion,
  etiqueta = 'Motivo de la baja',
  accion = 'Dar de baja',
  variante = 'secondary-danger',
  minimo = MOTIVO_BAJA_MIN,
}: MotivoBajaDialogProps) {
  const id = useId();
  const [motivo, setMotivo] = useState('');
  const [enviando, setEnviando] = useState(false);
  const largo = motivo.trim().length;
  const valido = largo >= minimo && largo <= MOTIVO_BAJA_MAX;

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
          <DialogTitle>{titulo}</DialogTitle>
          <DialogDescription>
            {descripcion ??
              `${nombreArchivo}. La baja es irreversible; el archivo se conserva para auditoría pero deja de estar disponible para descarga.`}
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-1.5">
          <Label htmlFor={id} className="text-xs font-medium text-ink-strong">
            {etiqueta}
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
            Entre {minimo} y {MOTIVO_BAJA_MAX} caracteres ({largo}).
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
            variant={variante}
            onClick={confirmar}
            disabled={!valido || enviando}
            title={
              valido
                ? undefined
                : `Escribe un motivo de al menos ${minimo} caracteres.`
            }
            data-action="confirmar-baja"
          >
            {enviando && <Loader2 className="animate-spin" />}
            {accion}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
