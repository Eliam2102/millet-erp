import { ShieldAlert } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import {
  NivelAutorizacion,
  nivelPendienteToString,
} from '@/features/compras/api/types';

/**
 * <c>&lt;NivelPendienteBadge/&gt;</c> — badge del nivel de autorización que
 * falta firmar en una RQ <c>EnAutorizacion</c> (PR-A). "Falta N1" / "Falta
 * N2", para que en la bandeja de pendientes el autorizador distinga de un
 * vistazo qué le toca (y no confunda una RQ que ya pasó N1 con una que aún
 * no empieza).
 *
 * <para>Colores propios (sky para N1, índigo para N2), distintos del ámbar
 * de "En autorización" que pinta <c>&lt;EstadoBadge/&gt;</c> al lado. Pares
 * fondo/texto AA verificados.</para>
 */
const COLORES: Record<NivelAutorizacion, string> = {
  [NivelAutorizacion.Nivel1]: 'border-sky-300 bg-sky-50 text-sky-700',
  [NivelAutorizacion.Nivel2]: 'border-indigo-300 bg-indigo-50 text-indigo-700',
};

export interface NivelPendienteBadgeProps {
  nivel: NivelAutorizacion;
  className?: string;
}

export function NivelPendienteBadge({
  nivel,
  className,
}: NivelPendienteBadgeProps) {
  return (
    <Badge
      variant="outline"
      className={cn('gap-1 font-medium', COLORES[nivel], className)}
      data-nivel-pendiente={nivel}
    >
      <ShieldAlert className="h-3.5 w-3.5" />
      {nivelPendienteToString(nivel)}
    </Badge>
  );
}
