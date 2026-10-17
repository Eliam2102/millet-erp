import { Badge } from '@/components/ui/badge';
import type { EstadoEjecucionSync } from '@/modules/datos-maestros/api/types';

const ESTADO_UI: Record<EstadoEjecucionSync, { texto: string; variante: 'success' | 'warning' | 'danger' | 'info' | 'neutral' }> = {
  Completa: { texto: 'Completa', variante: 'success' },
  Parcial: { texto: 'Parcial', variante: 'warning' },
  Fallida: { texto: 'Fallida', variante: 'danger' },
  EnCurso: { texto: 'En curso', variante: 'info' },
  Pendiente: { texto: 'Pendiente', variante: 'info' },
  Cancelada: { texto: 'Cancelada', variante: 'neutral' },
};

/** Estado de una corrida de sincronización con A+W: Badge con texto (no solo color). */
export function EstadoCorridaBadge({ estado }: { estado: EstadoEjecucionSync }) {
  const ui = ESTADO_UI[estado] ?? { texto: estado, variante: 'neutral' as const };
  return <Badge variant={ui.variante}>{ui.texto}</Badge>;
}
