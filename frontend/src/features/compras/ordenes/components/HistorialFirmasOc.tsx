import { Badge } from '@/components/ui/badge';
import { DateTimeDisplay } from '@/components/erp';
import type { OrdenCompraDetalleResponse } from '../api/types';
import { agruparCiclosAutorizacion } from '../lib/ciclos-autorizacion';

export function HistorialFirmasOc({ oc, resolverNombre }: {
  oc: Pick<OrdenCompraDetalleResponse, 'cicloAutorizacion' | 'autorizaciones'>;
  resolverNombre: (id: string) => string;
}) {
  const ciclos = agruparCiclosAutorizacion(oc.autorizaciones ?? []);
  return <section className="space-y-4 rounded-lg bg-surface-card p-4 shadow-card-flat" aria-label="Historial de firmas de autorización">
    <h2 className="text-md font-semibold">Firmas de autorización · ciclo {oc.cicloAutorizacion ?? 1}</h2>
    {!ciclos.length && <p className="text-sm text-ink-muted">Sin firmas registradas.</p>}
    {ciclos.map(({ ciclo, firmas }) => <div key={ciclo} className="space-y-2">
      <h3 className="text-sm font-semibold">Ciclo {ciclo}</h3>
      {firmas.map((firma) => <div key={firma.id} className="space-y-1 border-b border-line-divider pb-3 text-sm">
        <p>Nivel {firma.nivel} · {resolverNombre(firma.usuarioId)} · <DateTimeDisplay value={firma.fechaHora} variant="datetime" />{' '}
          <Badge variant={firma.resultado === 1 ? 'success' : 'danger'}>{firma.resultado === 1 ? 'Autorizado' : 'Rechazado'}</Badge>
        </p>
        {firma.resultado === 2 && <>
          <p className="text-ink-secondary">Motivo: {firma.motivoRechazoNombre ?? firma.motivoRechazoTexto ?? 'No disponible'}</p>
          {firma.motivoRechazoNombre && firma.motivoRechazoTexto && <p className="text-ink-secondary">{firma.motivoRechazoTexto}</p>}
        </>}
        {firma.notas && <p className="text-ink-secondary">{firma.notas}</p>}
      </div>)}
    </div>)}
  </section>;
}
