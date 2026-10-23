import type { ReactNode } from 'react';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';

export interface KpiCardProps {
  etiqueta: string;
  valor: ReactNode;
  subdato?: ReactNode;
  tono?: 'success' | 'warning' | 'danger' | 'info' | 'neutral';
  href?: string;
  prioridad?: 'critico' | 'atencion';
}
const tonos = {
  success: 'bg-success',
  warning: 'bg-warning',
  danger: 'bg-danger',
  info: 'bg-info',
  neutral: 'bg-neutral-fg',
};
/** Indicador compacto con acceso opcional a su listado y prioridad visible. */
export function KpiCard({
  etiqueta,
  valor,
  subdato,
  tono = 'neutral',
  href,
  prioridad,
}: KpiCardProps) {
  const Tag = href ? 'a' : 'div';
  return (
    <Tag
      href={href}
      className={cn(
        'flex flex-col gap-1.5 rounded-lg bg-surface-card px-4 py-3.5 text-ink shadow-card',
        prioridad === 'critico' && 'ring-1 ring-danger-ring',
      )}
    >
      <div className="flex items-center justify-between gap-2">
        <span className="flex items-center gap-2 text-xs text-ink-muted">
          <span aria-hidden="true" className={cn('size-2 rounded-[2px]', tonos[tono])} />
          {etiqueta}
        </span>
        {prioridad && (
          <Badge
            variant={prioridad === 'critico' ? 'danger' : 'warning'}
            className="h-5 text-2xs font-semibold"
          >
            {prioridad === 'critico' ? 'Crítico' : 'Atención'}
          </Badge>
        )}
      </div>
      <div className="flex flex-wrap items-baseline gap-2">
        <span className="text-3xl font-semibold tabular-nums">{valor}</span>
        {subdato != null && <span className="text-xs text-ink-muted">{subdato}</span>}
      </div>
    </Tag>
  );
}
