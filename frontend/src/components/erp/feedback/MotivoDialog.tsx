import { useId, useState, type ReactNode } from 'react';
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

export interface MotivoDialogProps {
  titulo: string;
  descripcion: ReactNode;
  etiqueta: string;
  /** Mínimo y máximo de caracteres del motivo (texto libre). */
  min: number;
  max: number;
  /** Verbo específico del botón de confirmación (DS §6). */
  verbo: string;
  /** Lanza para mostrar el error en el diálogo (se queda abierto). */
  onConfirmar: (motivo: string) => Promise<void>;
  onCerrar: () => void;
  /** Mensaje de error ya traducido por el caller. */
  error: string | null;
  variante?: 'secondary-danger' | 'default';
}

/**
 * Diálogo genérico de acción con motivo de texto libre obligatorio.
 * Radix resuelve foco atrapado, Esc y devolución del foco.
 */
export function MotivoDialog({
  titulo,
  descripcion,
  etiqueta,
  min: minimo,
  max,
  verbo: accion,
  onConfirmar,
  onCerrar,
  error,
  variante = 'secondary-danger',
}: MotivoDialogProps) {
  const id = useId();
  const [motivo, setMotivo] = useState('');
  const [enviando, setEnviando] = useState(false);
  const largo = motivo.trim().length;
  const valido = largo >= minimo && largo <= max;

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
      <DialogContent closeLabel="Cerrar" data-component="motivo-dialog">
        <DialogHeader>
          <DialogTitle>{titulo}</DialogTitle>
          <DialogDescription>
            {descripcion}
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
            maxLength={max}
            onChange={(e) => setMotivo(e.target.value)}
            aria-describedby={`${id}-ayuda`}
            aria-invalid={largo > 0 && !valido}
            disabled={enviando}
          />
          <p id={`${id}-ayuda`} className="text-xs text-ink-muted">
            Entre {minimo} y {max} caracteres ({largo}).
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
            data-action="confirmar-motivo"
          >
            {enviando && <Loader2 className="animate-spin" />}
            {accion}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
