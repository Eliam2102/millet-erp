import { Card } from '@/components/ui/card';
import { formatoMontoInicio, type PiezaResuelta } from '../api/usePiezasInicio';
import { AccesosRapidos } from './AccesosRapidos';

export function VisualesInicio({ items }: { items: PiezaResuelta[] }) {
  if (!items.length) return <AccesosRapidos titulo="Accesos de tu módulo" principal />;
  const ordenadas = [...items].sort((a, b) => Number(a.pieza.id === 'flujo') - Number(b.pieza.id === 'flujo'));
  const filas = ordenadas.map(i => items.length > 1 && i.pieza.id !== 'flujo' ? 'auto' : 'minmax(0, 1fr)').join(' ');
  return <aside aria-label="Resumen visual" className="grid min-h-0 min-w-0 gap-3" style={{ gridTemplateRows: filas }}>
    {ordenadas.map(({ pieza, data, estado }) => <Card key={pieza.id} className="flex min-h-0 min-w-0 flex-col bg-surface-card p-3 shadow-card-flat">
      <h2 className="text-lg font-semibold">{pieza.titulo}</h2>
      {estado !== 'listo' ? <p role="status" className="mt-2 text-xs text-ink-muted">{estado === 'error' ? 'No se pudo cargar. Por confirmar.' : 'Cargando…'}</p> : <>
        <p className="mt-1 text-xs text-ink-muted">{data?.contexto}</p>
        {!!data?.etapas?.length && <div className="mt-3 flex min-w-0 gap-1.5">{data.etapas.map(e => <div key={e.etiqueta} className="min-w-0 flex-1 rounded-md bg-surface-subtle p-2 text-center">
          <b className="block text-2xl font-semibold tabular-nums">{e.total}</b><span className="text-2xs text-ink-muted">{e.etiqueta}</span>
        </div>)}</div>}
        {!!data?.serie?.length && <div className="mt-3 flex min-h-28 flex-1 flex-col gap-2 lg:min-h-0">
          <div className="flex min-h-0 flex-1 items-end gap-3" aria-label="Cobros y pagos por día">
            {data.serie.map(d => {
              const max = Math.max(1, ...data.serie!.flatMap(s => [s.cobros, s.pagos]));
              return <div key={d.dia} className="flex h-full min-w-0 flex-1 flex-col justify-end gap-1">
                {/* Barras con posición absoluta: el alto en % no se resuelve dentro de contenedores flex sin alto definido. */}
                <div className="relative min-h-0 flex-1" title={`${d.dia}: cobros ${formatoMontoInicio(d.cobros)}, pagos ${formatoMontoInicio(d.pagos)}`}>
                  <span className="absolute bottom-0 left-0 w-[calc(50%-2px)] rounded-sm bg-success" style={{ height: `${d.cobros / max * 100}%` }} />
                  <span className="absolute bottom-0 right-0 w-[calc(50%-2px)] rounded-sm bg-info" style={{ height: `${d.pagos / max * 100}%` }} />
                </div><span className="text-center text-2xs text-ink-muted">{d.dia}</span>
              </div>;
            })}
          </div>
          <p className="text-xs text-ink-muted"><span className="text-success-fg">Cobros</span> · <span className="text-info-fg">Pagos</span></p>
        </div>}
      </>}
    </Card>)}
  </aside>;
}
