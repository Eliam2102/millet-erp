import { Loader2 } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { esApiError } from '@/lib/api';
import { fechaCorridaAw } from '@/modules/datos-maestros/api/sincronizacion-aw';
import {
  useReprocesarDeadLetterRetornoAw,
  useResumenRetornoAw,
} from '@/modules/datos-maestros/api/retorno-aw';

/**
 * M-160: avisos del ERP a A+W (factura, pago, estatus) que esperan porque A+W no respondía.
 * Se reintentan solos (1, 5, 15 y 60 min, luego cada hora); aquí se ven y se reprocesa la dead-letter.
 */
export function AvisosRetornoAwSection() {
  const resumen = useResumenRetornoAw(true);
  const reprocesar = useReprocesarDeadLetterRetornoAw();
  const r = resumen.data;

  function correr() {
    reprocesar.mutate(undefined, {
      onSuccess: (x) =>
        toast.success(
          `Dead-letter: ${x.aplicados} aplicados, ${x.pendientes} pendientes, ${x.invalidos} inválidos, ${x.fallidos} con falla.`,
        ),
      onError: (e) =>
        toast.error(esApiError(e) ? (e.problem.detail ?? e.problem.title) : 'No se pudo reprocesar. Reintenta.'),
    });
  }

  return (
    <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat" aria-label="Avisos a A+W">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-semibold">Avisos a A+W</h2>
        <Button variant="outline" aria-busy={reprocesar.isPending} disabled={reprocesar.isPending} onClick={correr}>
          {reprocesar.isPending && (
            <Loader2 aria-hidden className="mr-1.5 h-4 w-4 animate-spin motion-reduce:animate-none" />
          )}
          Reprocesar dead-letter
        </Button>
      </div>
      {resumen.isError && (
        <p role="alert" className="text-danger-fg">No se pudo cargar el estado de los avisos.</p>
      )}
      {r && (
        <dl className="grid gap-3 sm:grid-cols-3">
          <div>
            <dt className="text-xs text-ink-muted">Pendientes</dt>
            <dd className="flex items-center gap-2 tabular-nums">
              {r.pendientes}
              {r.alerta ? (
                <Badge variant="danger">Más de 4 h sin A+W</Badge>
              ) : r.pendientes > 0 ? (
                <Badge variant="warning">Reintentando</Badge>
              ) : (
                <Badge variant="success">Al día</Badge>
              )}
              {r.detenidos > 0 && <Badge variant="danger">{r.detenidos} detenidos</Badge>}
            </dd>
          </div>
          <div>
            <dt className="text-xs text-ink-muted">Más antiguo</dt>
            <dd>
              {r.masAntiguoEnUtc
                ? `${fechaCorridaAw(r.masAntiguoEnUtc)} · ${r.masAntiguoIntentos} intentos · siguiente ${fechaCorridaAw(r.masAntiguoProximoIntentoUtc)}`
                : '—'}
            </dd>
          </div>
          <div>
            <dt className="text-xs text-ink-muted">Último error</dt>
            <dd className="break-words text-ink-secondary">{r.masAntiguoUltimoError ?? '—'}</dd>
          </div>
        </dl>
      )}
    </section>
  );
}
