import { useState } from 'react';
import { TriangleAlert } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useEjercicios } from '../../api/periodos';
import { useAnularPoliza } from '../../api/polizas';
import type { AnularPolizaRequest, Poliza } from '../../api/polizas-types';
import { SELECT_CLASS } from '../../lib/estilos';
import {
  CODIGO_PERIODO_ORIGINAL_ABIERTO, CODIGO_REQUIERE_PERIODO, MOTIVO_ANULACION_MAXIMO, MOTIVO_ANULACION_MINIMO, etiquetaPeriodo, mensajePoliza,
} from '../../lib/polizas';

interface Props {
  poliza: Poliza | null;
  onClose: () => void;
  /** Recibe la NUEVA póliza de anulación. */
  onAnulada: (anulacion: Poliza) => void;
}

/**
 * Anular una póliza autorizada (K10.3): pide motivo y genera otra póliza del mismo tipo con las partidas en negativo.
 * Si el periodo de la original ya no admite pólizas, el servidor lo indica y se elige un periodo abierto de destino.
 */
export function AnularPolizaDialog({ poliza, onClose, onAnulada }: Props) {
  const anular = useAnularPoliza();
  const keyFor = useBodyScopedIdempotencyKey();
  const [motivo, setMotivo] = useState('');
  const [pedirPeriodo, setPedirPeriodo] = useState(false);
  const [destino, setDestino] = useState('');
  const [error, setError] = useState('');
  const ejercicios = useEjercicios({ enabled: pedirPeriodo });

  const abiertos = (ejercicios.data ?? [])
    .flatMap((e) => e.periodos)
    .filter((p) => p.estado === 'Abierto')
    .sort((a, b) => a.anio - b.anio || a.numero - b.numero);

  const largo = motivo.trim().length;
  const motivoCorto = largo < MOTIVO_ANULACION_MINIMO;
  const faltaDestino = pedirPeriodo && !destino;
  const motivoDeshabilitado = motivoCorto
    ? `El motivo lleva al menos ${MOTIVO_ANULACION_MINIMO} caracteres`
    : faltaDestino ? 'Elige el periodo abierto donde se registrará la anulación' : undefined;

  function cerrar() {
    if (anular.isPending) return;
    setMotivo(''); setPedirPeriodo(false); setDestino(''); setError('');
    onClose();
  }

  function confirmar() {
    if (!poliza || motivoDeshabilitado) return;
    setError('');
    const [anio, numero] = destino ? destino.split('-').map(Number) : [null, null];
    const body: AnularPolizaRequest = { motivo: motivo.trim(), periodoAnio: pedirPeriodo ? anio : null, periodoNumero: pedirPeriodo ? numero : null };
    anular.mutate(
      { poliza, body, idempotencyKey: keyFor(body) },
      {
        onSuccess: (nueva) => {
          setMotivo(''); setPedirPeriodo(false); setDestino('');
          onAnulada(nueva);
        },
        onError: (e) => {
          if (esApiError(e) && e.code === CODIGO_REQUIERE_PERIODO) setPedirPeriodo(true);
          if (esApiError(e) && e.code === CODIGO_PERIODO_ORIGINAL_ABIERTO) { setPedirPeriodo(false); setDestino(''); }
          setError(mensajePoliza(e));
        },
      },
    );
  }

  return (
    <Dialog open={!!poliza} onOpenChange={(o) => { if (!o) cerrar(); }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Anular póliza {poliza?.folio}</DialogTitle>
          <DialogDescription>
            Se generará otra póliza del mismo tipo, con folio nuevo y las mismas partidas en negativo («Anulación de {poliza?.folio}»).
            La original queda como Anulada y no se modifica.
          </DialogDescription>
        </DialogHeader>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="anular-motivo">Motivo</Label>
          <Textarea
            id="anular-motivo" rows={3} className="resize-none" maxLength={MOTIVO_ANULACION_MAXIMO} value={motivo}
            aria-describedby="anular-motivo-ayuda" onChange={(e) => setMotivo(e.target.value)}
          />
          <span id="anular-motivo-ayuda" className="text-xs text-ink-muted">
            {largo}/{MOTIVO_ANULACION_MAXIMO} · mínimo {MOTIVO_ANULACION_MINIMO} caracteres.
          </span>
        </div>
        {pedirPeriodo && (
          <div className="flex flex-col gap-1.5">
            <p role="note" className="flex items-start gap-2 rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg">
              <TriangleAlert className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} aria-hidden="true" />
              <span>
                El periodo {poliza ? etiquetaPeriodo(poliza.periodoAnio, poliza.periodoNumero) : ''} ya no admite pólizas. Elige un periodo
                abierto donde registrar la anulación.
              </span>
            </p>
            <Label htmlFor="anular-periodo">Periodo de la anulación</Label>
            <select id="anular-periodo" className={SELECT_CLASS} value={destino} onChange={(e) => setDestino(e.target.value)}>
              <option value="">{ejercicios.isLoading ? 'Cargando periodos…' : 'Elegir periodo abierto'}</option>
              {abiertos.map((p) => <option key={p.id} value={`${p.anio}-${p.numero}`}>{etiquetaPeriodo(p.anio, p.numero)}</option>)}
            </select>
            {ejercicios.isSuccess && abiertos.length === 0 && (
              <span className="text-xs text-danger-fg">No hay periodos abiertos. Pide a Contabilidad abrir uno en Periodos contables.</span>
            )}
          </div>
        )}
        {error && <p role="alert" className="rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">{error}</p>}
        <DialogFooter>
          <Button variant="ghost" onClick={cerrar} disabled={anular.isPending}>Cancelar</Button>
          <Button variant="secondary-danger" onClick={confirmar} disabled={!!motivoDeshabilitado || anular.isPending} title={motivoDeshabilitado}>
            {anular.isPending ? 'Anulando…' : 'Anular póliza'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
