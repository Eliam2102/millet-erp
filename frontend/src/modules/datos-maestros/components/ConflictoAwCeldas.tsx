import { Badge } from '@/components/ui/badge';
import {
  ESTADO_CAMBIO_AW,
  resumenRfcCompartido,
  type CambioMaestroAw,
} from '@/modules/datos-maestros/api/sincronizacion-aw';

export function EstadoConflictoAwBadge({ estado }: { estado: CambioMaestroAw['estado'] }) {
  const e = ESTADO_CAMBIO_AW[estado];
  return <Badge variant={e.variante}>{e.texto}</Badge>;
}

/** Causa corta: el RFC repetido en mono y cuántos clientes lo comparten; el resto, la causa tal cual. */
export function CausaConflictoAw({ c }: { c: CambioMaestroAw }) {
  if (c.tipo !== 'RfcCompartido' || !c.valorDespues) return <>{c.causa}</>;
  const { rfc, refs, clientes } = resumenRfcCompartido(c);
  return (
    <div className="space-y-0.5">
      <p>
        <span className="font-mono font-medium">{rfc}</span>
        <span className="text-ink-muted">{` · ${clientes} clientes`}</span>
      </p>
      {refs.length > 0 && <p className="text-xs text-ink-muted">{refs.join(' · ')}</p>}
    </div>
  );
}
