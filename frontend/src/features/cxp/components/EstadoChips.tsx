import { Badge } from '@/components/ui/badge';
import type {
  EstadoNotaCredito,
  EstadoAnticipo,
  EstadoComprobacionGastos,
  EstadoSolicitudViaticos,
  EstadoTarjeta,
  EstadoMovimientoTc,
  EstadoCuentaTcStatus,
  EstadoNotaCargo,
} from '@/features/cxp/api/types';
import {
  ESTADO_NOTA_CREDITO,
  ESTADO_ANTICIPO,
  ESTADO_COMPROBACION,
  ESTADO_SOLICITUD_VIATICOS,
  ESTADO_TARJETA,
  ESTADO_MOVIMIENTO_TC,
  ESTADO_CUENTA_TC,
  ESTADO_NOTA_CARGO,
} from '@/features/cxp/lib/estado-variante';

/** Chips de estado de CxP (NC, anticipo, comprobación, viáticos, TC, nota de cargo); color en `lib/estado-variante`. */

export function EstadoNotaCreditoChip({ estado }: { estado: EstadoNotaCredito }) {
  const { etiqueta, variante } = ESTADO_NOTA_CREDITO[estado];
  return <Badge variant={variante}>{etiqueta}</Badge>;
}

export function EstadoAnticipoChip({ estado }: { estado: EstadoAnticipo }) {
  const { etiqueta, variante } = ESTADO_ANTICIPO[estado];
  return <Badge variant={variante}>{etiqueta}</Badge>;
}

export function EstadoComprobacionChip({ estado }: { estado: EstadoComprobacionGastos }) {
  const { etiqueta, variante } = ESTADO_COMPROBACION[estado];
  return <Badge variant={variante}>{etiqueta}</Badge>;
}

export function EstadoSolicitudViaticosChip({ estado }: { estado: EstadoSolicitudViaticos }) {
  const { etiqueta, variante } = ESTADO_SOLICITUD_VIATICOS[estado];
  return <Badge variant={variante}>{etiqueta}</Badge>;
}

export function EstadoTarjetaChip({ estado }: { estado: EstadoTarjeta }) {
  const { etiqueta, variante } = ESTADO_TARJETA[estado];
  return <Badge variant={variante}>{etiqueta}</Badge>;
}

export function EstadoMovimientoTcChip({ estado }: { estado: EstadoMovimientoTc }) {
  const { etiqueta, variante } = ESTADO_MOVIMIENTO_TC[estado];
  return <Badge variant={variante}>{etiqueta}</Badge>;
}

export function EstadoCuentaTcChip({ estado }: { estado: EstadoCuentaTcStatus }) {
  const { etiqueta, variante } = ESTADO_CUENTA_TC[estado];
  return <Badge variant={variante}>{etiqueta}</Badge>;
}

export function EstadoNotaCargoChip({ estado }: { estado: EstadoNotaCargo }) {
  const { etiqueta, variante } = ESTADO_NOTA_CARGO[estado];
  return <Badge variant={variante}>{etiqueta}</Badge>;
}
