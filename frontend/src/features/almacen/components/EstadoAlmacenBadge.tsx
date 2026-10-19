import { Badge } from '@/components/ui/badge';
import {
  ESTADO_CONTEO,
  ESTADO_DEVOLUCION_PROVEEDOR,
  ESTADO_MOVIMIENTO,
  ESTATUS_CATALOGO,
  NIVEL_REORDEN,
} from '@/features/almacen/lib/estado-variante';
import type {
  EstadoConteo,
  EstadoDevolucionProveedor,
  EstadoMovimiento,
  EstatusCatalogo,
  NivelReorden,
} from '@/features/almacen/api/types';

/** Badge único de estado de Almacén; el color sale de `lib/estado-variante`. */
export type EstadoAlmacenBadgeProps =
  | { tipo: 'movimiento'; estado: EstadoMovimiento }
  | { tipo: 'devolucion-proveedor'; estado: EstadoDevolucionProveedor }
  | { tipo: 'conteo'; estado: EstadoConteo }
  | { tipo: 'catalogo'; estado: EstatusCatalogo }
  | { tipo: 'nivel-reorden'; estado: NivelReorden };

export function EstadoAlmacenBadge(props: EstadoAlmacenBadgeProps) {
  const info =
    props.tipo === 'movimiento' ? ESTADO_MOVIMIENTO[props.estado]
    : props.tipo === 'devolucion-proveedor' ? ESTADO_DEVOLUCION_PROVEEDOR[props.estado]
    : props.tipo === 'conteo' ? ESTADO_CONTEO[props.estado]
    : props.tipo === 'catalogo' ? ESTATUS_CATALOGO[props.estado]
    : NIVEL_REORDEN[props.estado];
  return <Badge variant={info.variante}>{info.etiqueta}</Badge>;
}
