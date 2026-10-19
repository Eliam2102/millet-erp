import { Badge } from '@/components/ui/badge';
import type { EstadoPasivo } from '@/features/cxp/api/types';
import { ESTADO_PASIVO } from '@/features/cxp/lib/estado-variante';

/** Badge de estado del pasivo/factura; color en `lib/estado-variante`. */
export function EstadoPasivoChip({ estado }: { estado: EstadoPasivo }) {
  const { etiqueta, variante } = ESTADO_PASIVO[estado];
  return <Badge variant={variante}>{etiqueta}</Badge>;
}
