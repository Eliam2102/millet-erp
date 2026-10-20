import { Badge } from '@/components/ui/badge';
import { estadoUi } from '../lib/estados-ui';

export function EstadoFacturacionBadge({
  entidad,
  estado,
}: {
  entidad: string;
  estado: string | null;
}) {
  const { tono, texto } = estadoUi(entidad, estado);
  return <Badge variant={tono}>{texto}</Badge>;
}
