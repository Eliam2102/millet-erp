import { Badge } from '@/components/ui/badge';
import type { EstadoPoliza, RelacionPoliza } from '../../api/polizas-types';
import { ETIQUETA_RELACION } from '../../lib/polizas';

import { ESTADOS_POLIZA } from '../../lib/estados-ui';

interface Props {
  estado: EstadoPoliza;
  esAnulacion?: boolean;
  /** K10.4: reversa automática o reclasificación de otra póliza. */
  relacion?: RelacionPoliza | null;
  /** K10.4: generada por una plantilla recurrente. */
  esRecurrente?: boolean;
}

/**
 * Estado de la póliza con su color semántico, más las marcas de origen: «Anulación» (K10.3), «Recurrente» y la relación
 * «Reversa»/«Reclasificación» (K10.4). Las marcas llevan texto visible, no solo color.
 */
export function EstadoPolizaBadge({ estado, esAnulacion, relacion, esRecurrente }: Props) {
  return (
    <span className="inline-flex flex-wrap gap-1">
      <Badge variant={ESTADOS_POLIZA[estado].tono}>{ESTADOS_POLIZA[estado].texto}</Badge>
      {(esAnulacion || relacion || esRecurrente) && (
        <Badge
          variant={esAnulacion || relacion === 'Reversa' ? 'warning' : 'info'}
          className="gap-1"
        >
          {esAnulacion && <span>Anulación</span>}
          {relacion && <span>{ETIQUETA_RELACION[relacion]}</span>}
          {esRecurrente && <span>Recurrente</span>}
        </Badge>
      )}
    </span>
  );
}
