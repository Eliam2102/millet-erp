import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import type { IndicadorInicio } from '../api/useResumenInicio';

export function FilaPendiente({ item }: { item: IndicadorInicio }) {
  const { fila, prioridad } = item;
  return (
    <li className="border-t border-line-row">
      <Link
        to={fila.to}
        search={fila.search ?? {}}
        aria-label={`${fila.titulo}: ${prioridad.motivo}`}
        className="grid min-h-16 grid-cols-[minmax(0,1fr)_auto_16px] items-center gap-x-3 gap-y-1 rounded-md px-4 py-3 text-ink hover:bg-surface-subtle focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand xl:grid-cols-[104px_minmax(0,1fr)_auto_84px_16px]"
      >
        <Badge
          variant="neutral"
          className="w-fit rounded-sm bg-surface-muted text-2xs font-medium text-ink-strong xl:w-26"
        >
          {fila.modulo}
        </Badge>
        <span className="col-start-1 row-start-2 min-w-0 xl:col-start-2 xl:row-start-1">
          <span className="block truncate text-sm font-medium" title={fila.titulo}>
            {fila.titulo}
          </span>
          <span className="block truncate text-xs text-ink-muted" title={fila.descripcion}>
            {fila.descripcion}
          </span>
        </span>
        <span className="col-start-2 row-start-2 text-right text-2xl font-semibold tabular-nums xl:col-start-3 xl:row-start-1">
          {prioridad.total}
        </span>
        <span
          className={`col-start-2 row-start-1 whitespace-nowrap text-right text-xs xl:col-start-4 ${prioridad.urgente ? 'text-danger-fg font-medium' : 'text-ink-muted'}`}
        >
          {prioridad.antiguedad}
        </span>
        <ChevronRight
          aria-hidden="true"
          className="col-start-3 row-span-2 row-start-1 size-4 text-ink-subtle xl:col-start-5 xl:row-span-1"
          strokeWidth={1.6}
        />
      </Link>
    </li>
  );
}
