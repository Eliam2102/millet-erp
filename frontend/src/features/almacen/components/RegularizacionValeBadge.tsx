import { Badge } from '@/components/ui/badge';
import { estadoRegularizacion } from '../lib/estado-regularizacion';

export function RegularizacionValeBadge({ salida }: {
  salida: { pendienteRegularizacion: boolean; vencido: boolean };
}) {
  const estado = estadoRegularizacion(salida);
  return <Badge variant={estado.variant}>{estado.texto}</Badge>;
}
