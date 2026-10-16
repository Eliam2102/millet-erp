import { Badge } from '@/components/ui/badge';
import type { EstadoPoliza } from '../../api/polizas-types';
import { VARIANTE_ESTADO_POLIZA } from '../../lib/polizas';

/** Estado de la póliza con su color semántico, más la marca «Anulación» en la póliza que anula a otra (K10.3). */
export function EstadoPolizaBadge({ estado, esAnulacion }: { estado: EstadoPoliza; esAnulacion?: boolean }) {
  return (
    <span className="inline-flex flex-wrap gap-1">
      <Badge variant={VARIANTE_ESTADO_POLIZA[estado]}>{estado}</Badge>
      {esAnulacion && <Badge variant="warning">Anulación</Badge>}
    </span>
  );
}
