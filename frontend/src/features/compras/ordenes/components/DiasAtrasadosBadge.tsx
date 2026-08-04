import { Clock, AlertTriangle, AlertCircle } from 'lucide-react';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;DiasAtrasadosBadge/&gt;</c> — badge color-coded según umbral
 * de días atrasados (UF7-PR1, FOC10).
 *
 * <list>
 *   <item><b>≤ 0 días</b>: verde — al día o por entregar.</item>
 *   <item><b>1-7 días</b>: amber — atrasada reciente.</item>
 *   <item><b>&gt; 7 días</b>: rose — atrasada crítica.</item>
 * </list>
 *
 * <para><b>A11y</b>: además del color, cada nivel tiene un ícono
 * distinto (Clock / AlertTriangle / AlertCircle) para color-blind.
 * El text es siempre legible aunque se pierda el color.</para>
 */
export interface DiasAtrasadosBadgeProps {
  diasAtrasados: number;
  className?: string;
}

export function DiasAtrasadosBadge({
  diasAtrasados,
  className,
}: DiasAtrasadosBadgeProps) {
  const { color, Icon, label } = resolverEstado(diasAtrasados);

  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-md px-1.5 py-0.5 text-xs font-medium ring-1 ring-inset',
        color,
        className,
      )}
      data-component="dias-atrasados-badge"
      data-nivel={diasAtrasados <= 0 ? 'al-dia' : diasAtrasados <= 7 ? 'reciente' : 'critica'}
      aria-label={label}
      title={label}
    >
      <Icon className="h-3 w-3" aria-hidden="true" />
      {diasAtrasados <= 0 ? 'Al día' : `+${diasAtrasados}d`}
    </span>
  );
}

function resolverEstado(dias: number) {
  if (dias <= 0) {
    return {
      color: 'bg-emerald-50 text-emerald-700 ring-emerald-200',
      Icon: Clock,
      label: 'Al día (no atrasada)',
    };
  }
  if (dias <= 7) {
    return {
      color: 'bg-amber-50 text-amber-700 ring-amber-200',
      Icon: AlertTriangle,
      label: `Atrasada reciente: ${dias} día${dias === 1 ? '' : 's'}`,
    };
  }
  return {
    color: 'bg-rose-50 text-rose-700 ring-rose-200',
    Icon: AlertCircle,
    label: `Atrasada crítica: ${dias} días`,
  };
}
