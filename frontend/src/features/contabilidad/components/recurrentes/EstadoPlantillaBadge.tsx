import { Badge } from '@/components/ui/badge';
import type { EstadoPlantillaRecurrente } from '../../api/recurrentes-types';
import { ESTADOS_PLANTILLA } from '../../lib/estados-ui';

/** Estado de la plantilla recurrente con su color semántico y texto visible. */
export function EstadoPlantillaBadge({ estado }: { estado: EstadoPlantillaRecurrente }) {
  return <Badge variant={ESTADOS_PLANTILLA[estado].tono}>{ESTADOS_PLANTILLA[estado].texto}</Badge>;
}
