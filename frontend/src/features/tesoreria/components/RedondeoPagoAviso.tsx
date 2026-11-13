import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { formatoMonto } from '@/features/tesoreria/lib/formato';
import { textoSituacionRedondeo, type SituacionRedondeo } from '@/features/tesoreria/lib/redondeo-pago';

export interface RedondeoPagoAvisoProps {
  folio: string | null;
  moneda: string;
  /** Saldo menos efectivo: positivo = se paga menos; negativo = se paga de más. */
  redondeo: number;
  situacion: SituacionRedondeo;
  motivo: string;
  onMotivoChange: (motivo: string) => void;
  onSolicitar: () => void;
  enviando: boolean;
}

/** M-326 (b): el importe difiere del saldo por ≤$0.99; el redondeo va a REDONDEO y lo autoriza el Gerente de Área. */
export function RedondeoPagoAviso({
  folio,
  moneda,
  redondeo,
  situacion,
  motivo,
  onMotivoChange,
  onSolicitar,
  enviando,
}: RedondeoPagoAvisoProps) {
  const puedeSolicitar = situacion.estado === 'sin-solicitud' || situacion.estado === 'Rechazada' ||
    (situacion.estado === 'Autorizada' && !situacion.autorizado);
  const variante = situacion.autorizado ? 'success' : situacion.estado === 'Rechazada' ? 'danger' : 'warning';
  return (
    <section
      role="note"
      aria-label={`Redondeo de ${folio ?? 'la factura'}`}
      className="space-y-2 rounded-md border bg-surface-card px-3 py-2.5 text-sm"
    >
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-medium">Redondeo · {folio ?? 'factura'}</span>
        <span className="font-mono tabular-nums">
          {redondeo > 0 ? 'A favor' : 'En contra'} {formatoMonto(Math.abs(redondeo), moneda)}
        </span>
        <Badge variant={variante}>{situacion.autorizado ? 'Autorizado' : (situacion.estado === 'sin-solicitud' ? 'Sin solicitar' : situacion.estado)}</Badge>
      </div>
      <p className="text-xs text-ink-muted">
        La diferencia se registra en la cuenta REDONDEO; el banco sale solo por el efectivo. {textoSituacionRedondeo(situacion, redondeo)}
      </p>
      {puedeSolicitar && (
        <div className="flex flex-wrap items-center gap-2">
          <Input
            className="min-w-56 flex-1"
            aria-label={`Motivo del redondeo de ${folio ?? 'la factura'}`}
            placeholder="Motivo del redondeo"
            maxLength={400}
            value={motivo}
            onChange={(e) => onMotivoChange(e.target.value)}
          />
          <Button type="button" size="sm" variant="outline" disabled={enviando || motivo.trim() === ''} onClick={onSolicitar}>
            {enviando ? 'Enviando…' : 'Solicitar autorización'}
          </Button>
        </div>
      )}
    </section>
  );
}
