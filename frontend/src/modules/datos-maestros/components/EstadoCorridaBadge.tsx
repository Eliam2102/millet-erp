import { Badge } from '@/components/ui/badge';
import type { EstadoEjecucionSync } from '../api/types';
import { estadoUi } from '../lib/estados-ui';
export function EstadoCorridaBadge({ estado }: { estado: EstadoEjecucionSync }) {
  const ui = estadoUi('corrida', estado);
  return <Badge variant={ui.tono}>{ui.texto}</Badge>;
}
