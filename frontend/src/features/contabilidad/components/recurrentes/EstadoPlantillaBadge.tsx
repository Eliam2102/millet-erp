import { Badge } from '@/components/ui/badge';
import type { EstadoPlantillaRecurrente } from '../../api/recurrentes-types';
import { VARIANTE_ESTADO_PLANTILLA } from '../../lib/recurrentes';

/** Estado de la plantilla recurrente con su color semántico y texto visible. */
export function EstadoPlantillaBadge({ estado }: { estado: EstadoPlantillaRecurrente }) {
  return <Badge variant={VARIANTE_ESTADO_PLANTILLA[estado]}>{estado}</Badge>;
}
