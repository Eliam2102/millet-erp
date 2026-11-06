import { Badge } from '@/components/ui/badge';
import type { EstadoIncidencia } from '@/features/incidencias/api/types';
import { ESTADO_INCIDENCIA } from './formato';

export function EstadoIncidenciaBadge({ estado }: { estado: EstadoIncidencia }) {
  const e = ESTADO_INCIDENCIA[estado] ?? { label: estado, tono: 'neutral' as const };
  return (
    <Badge variant={e.tono} data-estado={estado}>
      {e.label}
    </Badge>
  );
}
