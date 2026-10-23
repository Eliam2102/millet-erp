import { Badge } from '@/components/ui/badge';
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

export function DiasAtrasadosBadge({ diasAtrasados, className }: DiasAtrasadosBadgeProps) {
  const { tono, Icon, label } = resolverEstado(diasAtrasados);

  return (
    <Badge
      variant={tono}
      className={cn('gap-1', className)}
      data-component="dias-atrasados-badge"
      data-nivel={diasAtrasados <= 0 ? 'al-dia' : diasAtrasados <= 7 ? 'reciente' : 'critica'}
      aria-label={label}
      title={label}
    >
      <Icon className="h-3 w-3" aria-hidden="true" />
      {diasAtrasados <= 0 ? 'Al día' : `+${diasAtrasados}d`}
    </Badge>
  );
}

function resolverEstado(dias: number) {
  if (dias <= 0) {
    return {
      tono: 'success' as const,
      Icon: Clock,
      label: 'Al día (no atrasada)',
    };
  }
  if (dias <= 7) {
    return {
      tono: 'warning' as const,
      Icon: AlertTriangle,
      label: `Atrasada reciente: ${dias} día${dias === 1 ? '' : 's'}`,
    };
  }
  return {
    tono: 'danger' as const,
    Icon: AlertCircle,
    label: `Atrasada crítica: ${dias} días`,
  };
}
