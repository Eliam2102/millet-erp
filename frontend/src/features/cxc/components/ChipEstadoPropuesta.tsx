import { Badge } from '@/components/ui/badge';
import { EstadoCxcBadge } from './EstadoCxcBadge';
import type { EstadoPropuestaAplicacion } from '../api/types';

/** CXC-06: con <c>ajustePorAutorizar</c> la propuesta aún no llega a Tesorería; se distingue de «Propuesta». */
export function ChipEstadoPropuesta({
  estado,
  ajustePorAutorizar = false,
}: {
  estado: EstadoPropuestaAplicacion;
  ajustePorAutorizar?: boolean;
}) {
  if (ajustePorAutorizar) return <Badge variant="warning">Ajuste por autorizar</Badge>;
  return <EstadoCxcBadge entidad="propuesta" estado={estado} />;
}
