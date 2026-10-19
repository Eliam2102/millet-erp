import { Badge } from '@/components/ui/badge';
import {
  ESTADO_COMERCIAL_PROVEEDOR_LABELS,
  ESTADO_COMERCIAL_PROVEEDOR_VARIANT,
  type EstadoComercialProveedorOc,
} from '../api/types';

/** F1-COM-11: estado comercial de la OC según la respuesta del proveedor. */
export function EstadoComercialProveedorBadge({ estado }: { estado: EstadoComercialProveedorOc }) {
  return (
    <Badge variant={ESTADO_COMERCIAL_PROVEEDOR_VARIANT[estado]}>
      {ESTADO_COMERCIAL_PROVEEDOR_LABELS[estado]}
    </Badge>
  );
}
