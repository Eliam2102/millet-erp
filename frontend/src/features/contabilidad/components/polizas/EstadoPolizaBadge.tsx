import { Badge } from '@/components/ui/badge';
import type { EstadoPoliza, RelacionPoliza } from '../../api/polizas-types';
import { ETIQUETA_RELACION, VARIANTE_ESTADO_POLIZA } from '../../lib/polizas';

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
      <Badge variant={VARIANTE_ESTADO_POLIZA[estado]}>{estado}</Badge>
      {esAnulacion && <Badge variant="warning">Anulación</Badge>}
      {relacion && <Badge variant={relacion === 'Reversa' ? 'warning' : 'info'}>{ETIQUETA_RELACION[relacion]}</Badge>}
      {esRecurrente && <Badge variant="info">Recurrente</Badge>}
    </span>
  );
}
