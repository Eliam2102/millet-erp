import { Badge } from '@/components/ui/badge';
import { estadoUi } from '../lib/estados-ui';
export function EstadoCxcBadge({
  entidad,
  estado,
}: {
  entidad: Parameters<typeof estadoUi>[0];
  estado: string | number;
}) {
  const ui = estadoUi(entidad, estado);
  return <Badge variant={ui.tono}>{ui.texto}</Badge>;
}
