import { Bot } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import { OrigenRequisicion } from '@/features/compras/api/types';

/**
 * <c>&lt;OrigenBadge/&gt;</c> — chip "Sistema" para las RQs creadas por el
 * motor de reabasto (código = reorden, ADR-0047 PR5). Molde
 * <c>&lt;NivelPendienteBadge/&gt;</c> (Badge outline + icono).
 *
 * <para>No renderiza nada para origen <c>Manual</c> (el caso normal): solo
 * señala las automáticas, para que en la bandeja se distingan de un vistazo de
 * las capturadas por un usuario. Color violeta, distinto del ámbar/esmeralda
 * del <c>&lt;EstadoBadge/&gt;</c> vecino.</para>
 */
export interface OrigenBadgeProps {
  origen: OrigenRequisicion;
  className?: string;
}

export function OrigenBadge({ origen, className }: OrigenBadgeProps) {
  if (origen !== OrigenRequisicion.Sistema) return null;
  return (
    <Badge
      variant="outline"
      className={cn(
        'gap-1 border-violet-300 bg-violet-50 font-medium text-violet-700',
        className,
      )}
      data-origen="sistema"
      title="Requisición creada automáticamente por el motor de reabasto"
    >
      <Bot className="h-3.5 w-3.5" />
      Sistema
    </Badge>
  );
}
