import { Badge } from '@/components/ui/badge';
import { estadosExpediente } from '../lib/estados-ui';
export function EstadoExpedienteBadge({ estado }: { estado: 1 | 2 }) {
  const ui = estadosExpediente[estado];
  return <Badge variant={ui.tono}>{ui.texto}</Badge>;
}
