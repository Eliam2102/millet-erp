import type { BadgeProps } from '@/components/ui/badge';
import {
  EstadoAnticipo,
  EstadoAnticipoLabels,
  EstadoComprobacionGastos,
  EstadoComprobacionGastosLabels,
  EstadoCuentaTcStatus,
  EstadoCuentaTcStatusLabels,
  EstadoMovimientoTc,
  EstadoMovimientoTcLabels,
  EstadoNotaCargo,
  EstadoNotaCargoLabels,
  EstadoNotaCredito,
  EstadoNotaCreditoLabels,
  EstadoPasivo,
  EstadoPasivoLabels,
  EstadoSolicitudViaticos,
  EstadoSolicitudViaticosLabels,
  EstadoTarjeta,
  EstadoTarjetaLabels,
} from '@/features/cxp/api/types';

/**
 * Mapas `estado → { etiqueta, variante }` de CxP (DESIGN §2.1 y §7.2).
 * Etiquetas = las del API; el color sale solo de aquí, así lista y detalle
 * coinciden.
 */
export type Variante = NonNullable<BadgeProps['variant']>;
export type EstadoVarianteInfo = { etiqueta: string; variante: Variante };

function armar<E extends number>(
  labels: Record<E, string>,
  variantes: Record<E, Variante>,
): Record<E, EstadoVarianteInfo> {
  const r = {} as Record<E, EstadoVarianteInfo>;
  for (const k of Object.keys(variantes) as unknown as E[]) {
    r[k] = { etiqueta: labels[k], variante: variantes[k] };
  }
  return r;
}

export const ESTADO_PASIVO = armar(EstadoPasivoLabels, {
  [EstadoPasivo.Capturada]: 'neutral',
  [EstadoPasivo.EnRevision]: 'warning',
  [EstadoPasivo.Autorizada]: 'success',
  [EstadoPasivo.Pagada]: 'success',
  [EstadoPasivo.Cancelada]: 'neutral',
});

export const ESTADO_NOTA_CREDITO = armar(EstadoNotaCreditoLabels, {
  [EstadoNotaCredito.EnEspera]: 'warning',
  [EstadoNotaCredito.Abierta]: 'info',
  [EstadoNotaCredito.Aplicada]: 'success',
  [EstadoNotaCredito.Cancelada]: 'neutral',
});

export const ESTADO_ANTICIPO = armar(EstadoAnticipoLabels, {
  [EstadoAnticipo.Abierto]: 'info',
  [EstadoAnticipo.Amortizado]: 'success',
  [EstadoAnticipo.Cancelado]: 'neutral',
});

export const ESTADO_NOTA_CARGO = armar(EstadoNotaCargoLabels, {
  [EstadoNotaCargo.Borrador]: 'neutral',
  [EstadoNotaCargo.Autorizada]: 'info',
  [EstadoNotaCargo.Aplicada]: 'success',
  [EstadoNotaCargo.Formalizada]: 'success',
  [EstadoNotaCargo.Cancelada]: 'neutral',
});

export const ESTADO_COMPROBACION = armar(EstadoComprobacionGastosLabels, {
  [EstadoComprobacionGastos.Borrador]: 'neutral',
  [EstadoComprobacionGastos.PorRevisar]: 'warning',
  [EstadoComprobacionGastos.AutorizadaNivel1]: 'info',
  [EstadoComprobacionGastos.Autorizada]: 'info',
  [EstadoComprobacionGastos.Aplicada]: 'success',
  [EstadoComprobacionGastos.Rechazada]: 'danger',
});

export const ESTADO_SOLICITUD_VIATICOS = armar(EstadoSolicitudViaticosLabels, {
  [EstadoSolicitudViaticos.Solicitada]: 'neutral',
  [EstadoSolicitudViaticos.AutorizadaPorJefe]: 'info',
  [EstadoSolicitudViaticos.RequiereDireccionFinanzas]: 'warning',
  [EstadoSolicitudViaticos.AutorizadaCompleta]: 'info',
  [EstadoSolicitudViaticos.Anticipada]: 'info',
  [EstadoSolicitudViaticos.ComprobacionCapturada]: 'info',
  [EstadoSolicitudViaticos.Liquidada]: 'success',
  [EstadoSolicitudViaticos.Rechazada]: 'danger',
});

export const ESTADO_TARJETA = armar(EstadoTarjetaLabels, {
  [EstadoTarjeta.Activa]: 'success',
  [EstadoTarjeta.Bloqueada]: 'warning',
  [EstadoTarjeta.Cancelada]: 'neutral',
});

export const ESTADO_MOVIMIENTO_TC = armar(EstadoMovimientoTcLabels, {
  [EstadoMovimientoTc.Registrado]: 'info',
  [EstadoMovimientoTc.ConciliadoConEstadoCuenta]: 'info',
  [EstadoMovimientoTc.EnDisputa]: 'warning',
  [EstadoMovimientoTc.Reversado]: 'neutral',
  [EstadoMovimientoTc.PagadoAlBanco]: 'success',
});

export const ESTADO_CUENTA_TC = armar(EstadoCuentaTcStatusLabels, {
  [EstadoCuentaTcStatus.EnConciliacion]: 'warning',
  [EstadoCuentaTcStatus.Conciliado]: 'info',
  [EstadoCuentaTcStatus.Cerrado]: 'info',
  [EstadoCuentaTcStatus.PagadoBanco]: 'success',
});
