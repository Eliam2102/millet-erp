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
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import type { ReenviarSolicitudViaticosCommand } from '@/features/cxp/api/types';

/**
 * Corrección de una solicitud de viáticos devuelta: el empleado ajusta monto,
 * fechas y justificación y la reenvía a firma. El tope y si excede política los
 * recalcula el servidor.
 */
export function ReenviarViaticosDialog({
  inicial,
  comentario,
  error,
  onCerrar,
  onConfirmar,
}: {
  inicial: { montoSolicitado: number; fechaSalida?: string; fechaRegreso?: string };
  /** Comentario de quien devolvió, visible mientras se corrige. */
  comentario?: string | null;
  error: string | null;
  onCerrar: () => void;
  onConfirmar: (c: ReenviarSolicitudViaticosCommand) => Promise<void>;
}) {
  const id = useId();
  const [monto, setMonto] = useState(String(inicial.montoSolicitado));
  const [salida, setSalida] = useState(inicial.fechaSalida?.slice(0, 10) ?? '');
  const [regreso, setRegreso] = useState(inicial.fechaRegreso?.slice(0, 10) ?? '');
  const [justificacion, setJustificacion] = useState('');
  const [enviando, setEnviando] = useState(false);
  const montoNum = Number(monto);
  const valido = montoNum > 0 && salida !== '' && regreso !== '' && regreso >= salida;

  async function confirmar() {
    if (!valido || enviando) return;
    setEnviando(true);
    try {
      await onConfirmar({
        montoSolicitado: montoNum,
        fechaSalida: salida,
        fechaRegreso: regreso,
        justificacionExceso: justificacion.trim() || null,
      });
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Dialog open onOpenChange={(abierto) => !abierto && !enviando && onCerrar()}>
      <DialogContent closeLabel="Cerrar" data-component="reenviar-viaticos-dialog">
        <DialogHeader>
          <DialogTitle>Corregir y reenviar solicitud</DialogTitle>
          <DialogDescription>
            {comentario ? `Te la devolvieron con este comentario: «${comentario}».` : 'Corrige y reenvía a firma.'}
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-3">
          <div className="grid gap-1.5">
            <Label htmlFor={`${id}-monto`} className="text-xs font-medium text-ink-strong">
              Monto solicitado
            </Label>
            <Input
              id={`${id}-monto`}
              type="number"
              min="0"
              step="0.01"
              value={monto}
              onChange={(e) => setMonto(e.target.value)}
              disabled={enviando}
            />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div className="grid gap-1.5">
              <Label htmlFor={`${id}-salida`} className="text-xs font-medium text-ink-strong">
                Salida
              </Label>
              <Input id={`${id}-salida`} type="date" value={salida} onChange={(e) => setSalida(e.target.value)} disabled={enviando} />
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor={`${id}-regreso`} className="text-xs font-medium text-ink-strong">
                Regreso
              </Label>
              <Input id={`${id}-regreso`} type="date" value={regreso} onChange={(e) => setRegreso(e.target.value)} disabled={enviando} />
            </div>
          </div>
          <div className="grid gap-1.5">
            <Label htmlFor={`${id}-just`} className="text-xs font-medium text-ink-strong">
              Justificación (solo si excede la política)
            </Label>
            <Textarea
              id={`${id}-just`}
              rows={2}
              maxLength={2000}
              value={justificacion}
              onChange={(e) => setJustificacion(e.target.value)}
              disabled={enviando}
            />
          </div>
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
          <Button onClick={confirmar} disabled={!valido || enviando} data-action="confirmar-reenviar">
            {enviando && <Loader2 className="animate-spin" />}
            Reenviar a firma
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
