import type { BadgeProps } from '@/components/ui/badge';
import {
  EstadoConteo,
  EstadoConteoLabels,
  EstadoDevolucionProveedor,
  EstadoDevolucionProveedorLabels,
  EstadoMovimiento,
  EstadoMovimientoLabels,
  EstatusCatalogo,
  EstatusCatalogoLabels,
  NivelReorden,
  NivelReordenLabels,
} from '@/features/almacen/api/types';

/**
 * Mapas `estado → { etiqueta, variante }` de Almacén (DESIGN §2.1 y §7.2).
 * Un estado tiene un solo color en lista y detalle.
 */
export type EstadoVarianteInfo = {
  etiqueta: string;
  variante: NonNullable<BadgeProps['variant']>;
};

type Mapa<E extends number> = Record<E, EstadoVarianteInfo>;

export const ESTADO_MOVIMIENTO: Mapa<EstadoMovimiento> = {
  [EstadoMovimiento.Borrador]: { etiqueta: EstadoMovimientoLabels[EstadoMovimiento.Borrador], variante: 'neutral' },
  [EstadoMovimiento.Validado]: { etiqueta: EstadoMovimientoLabels[EstadoMovimiento.Validado], variante: 'info' },
  [EstadoMovimiento.Registrado]: { etiqueta: EstadoMovimientoLabels[EstadoMovimiento.Registrado], variante: 'success' },
  [EstadoMovimiento.Cancelado]: { etiqueta: EstadoMovimientoLabels[EstadoMovimiento.Cancelado], variante: 'neutral' },
};

export const ESTADO_DEVOLUCION_PROVEEDOR: Mapa<EstadoDevolucionProveedor> = {
  [EstadoDevolucionProveedor.Borrador]: { etiqueta: EstadoDevolucionProveedorLabels[EstadoDevolucionProveedor.Borrador], variante: 'neutral' },
  [EstadoDevolucionProveedor.EnAutorizacion]: { etiqueta: EstadoDevolucionProveedorLabels[EstadoDevolucionProveedor.EnAutorizacion], variante: 'warning' },
  [EstadoDevolucionProveedor.Autorizada]: { etiqueta: EstadoDevolucionProveedorLabels[EstadoDevolucionProveedor.Autorizada], variante: 'info' },
  [EstadoDevolucionProveedor.Registrada]: { etiqueta: EstadoDevolucionProveedorLabels[EstadoDevolucionProveedor.Registrada], variante: 'success' },
  [EstadoDevolucionProveedor.ConciliadaConNcFiscal]: { etiqueta: EstadoDevolucionProveedorLabels[EstadoDevolucionProveedor.ConciliadaConNcFiscal], variante: 'success' },
  [EstadoDevolucionProveedor.Rechazada]: { etiqueta: EstadoDevolucionProveedorLabels[EstadoDevolucionProveedor.Rechazada], variante: 'danger' },
};

export const ESTADO_CONTEO: Mapa<EstadoConteo> = {
  [EstadoConteo.Planificado]: { etiqueta: EstadoConteoLabels[EstadoConteo.Planificado], variante: 'neutral' },
  [EstadoConteo.EnCurso]: { etiqueta: EstadoConteoLabels[EstadoConteo.EnCurso], variante: 'info' },
  [EstadoConteo.EnConciliacion]: { etiqueta: EstadoConteoLabels[EstadoConteo.EnConciliacion], variante: 'warning' },
  [EstadoConteo.Aprobado]: { etiqueta: EstadoConteoLabels[EstadoConteo.Aprobado], variante: 'info' },
  [EstadoConteo.Aplicado]: { etiqueta: EstadoConteoLabels[EstadoConteo.Aplicado], variante: 'success' },
  [EstadoConteo.Rechazado]: { etiqueta: EstadoConteoLabels[EstadoConteo.Rechazado], variante: 'danger' },
};

export const ESTATUS_CATALOGO: Mapa<EstatusCatalogo> = {
  [EstatusCatalogo.Activo]: { etiqueta: EstatusCatalogoLabels[EstatusCatalogo.Activo], variante: 'success' },
  [EstatusCatalogo.Inactivo]: { etiqueta: EstatusCatalogoLabels[EstatusCatalogo.Inactivo], variante: 'neutral' },
  [EstatusCatalogo.Borrador]: { etiqueta: EstatusCatalogoLabels[EstatusCatalogo.Borrador], variante: 'warning' },
};

export const NIVEL_REORDEN: Mapa<NivelReorden> = {
  [NivelReorden.Sucursal]: { etiqueta: NivelReordenLabels[NivelReorden.Sucursal], variante: 'info' },
  [NivelReorden.Almacen]: { etiqueta: NivelReordenLabels[NivelReorden.Almacen], variante: 'neutral' },
};
