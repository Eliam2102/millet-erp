import { Link } from '@tanstack/react-router';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import type { IndicadorInicio } from '../api/useResumenInicio';
import { estiloNivel } from '../prioridad';

function TarjetaIndicador({ item }: { item: IndicadorInicio }) {
  const { fila, prioridad, estado } = item;
  const estilo = estiloNivel[prioridad.nivel];
  return (
    <Card
      className={`min-w-0 bg-surface-card shadow-card ${prioridad.nivel === 'critico' ? 'ring-1 ring-danger-ring' : ''}`}
    >
      <Link
        to={fila.to}
        search={fila.search ?? {}}
        aria-label={`${fila.titulo}: ${estado === 'listo' ? prioridad.motivo : estado === 'error' ? 'No se pudo cargar' : 'Cargando'}`}
        aria-busy={estado === 'cargando' || undefined}
        className="flex h-full flex-col gap-1.5 rounded-lg px-4 py-3.5 hover:bg-surface-subtle focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand"
      >
        <span className="flex items-center gap-2 text-xs text-ink-muted">
          <span aria-hidden="true" className={`size-2 shrink-0 rounded-[2px] ${estilo.punto}`} />
          {fila.titulo}
        </span>
        {estado === 'cargando' ? (
          <Skeleton className="h-7 w-12 bg-surface-muted" />
        ) : estado === 'error' ? (
          <span className="text-xs text-danger-fg">No se pudo cargar</span>
        ) : (
          <>
            <span className="flex flex-wrap items-baseline gap-x-2">
              <span className="text-3xl font-semibold tabular-nums text-ink">
                {prioridad.total}
              </span>
              <span className="text-xs text-ink-muted">{fila.modulo}</span>
            </span>
            <span className={`text-xs font-medium ${estilo.texto}`}>{prioridad.motivo}</span>
            {estilo.etiqueta && (
              <Badge
                variant={estilo.variante}
                className="mt-auto h-5 self-start text-2xs font-semibold"
              >
                {estilo.etiqueta}
              </Badge>
            )}
          </>
        )}
      </Link>
    </Card>
  );
}

export function IndicadoresInicio({ items }: { items: IndicadorInicio[] }) {
  return (
    <section aria-labelledby="indicadores-inicio" className="min-w-0 space-y-3 lg:col-span-2">
      <div className="flex items-center gap-4">
        <h2 id="indicadores-inicio" className="text-lg font-semibold">
          Indicadores
        </h2>
        <div className="h-px flex-1 bg-line" aria-hidden="true" />
      </div>
      {items.length ? (
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
          {items.map((item) => (
            <TarjetaIndicador key={item.fila.id} item={item} />
          ))}
        </div>
      ) : (
        <p className="text-xs text-ink-muted">No hay indicadores disponibles para tu rol.</p>
      )}
    </section>
  );
}
