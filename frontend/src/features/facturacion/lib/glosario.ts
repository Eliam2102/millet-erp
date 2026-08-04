import {
  ComportamientoFiscal,
  EstadoPedidoFacturable,
  EstadoTimbrado,
  OrigenPedido,
} from '@/features/facturacion/api/types';

/**
 * Etiquetas legibles de los enums del módulo Facturación. Centraliza
 * los textos en español para badges, selects y la página de ayuda
 * (mismo rol que <c>glosario.ts</c> de Compras/CxP).
 *
 * <para>Los estados/orígenes de la bandeja de pedidos llegan del backend
 * como string (<c>.ToString()</c> del enum: "Importado", "Aw", …); por
 * eso hay maps por nombre además de por valor numérico.</para>
 */

export const ETIQUETA_ESTADO_PEDIDO: Record<string, string> = {
  Importado: 'Importado',
  Bloqueado: 'En facturación',
  Facturado: 'Facturado',
  Cancelado: 'Cancelado',
  Excepcion: 'Excepción',
};

export const ETIQUETA_ORIGEN_PEDIDO: Record<string, string> = {
  Aw: 'A+W',
  PlantaPintura: 'Planta Pintura',
  Manual: 'Manual',
};

// Canal de venta (FAC-ING-PR3): ya NO hay etiquetas hardcodeadas — los
// selects consumen useCanalesVenta() (catálogo administrable) y los
// detalles muestran el `canalVenta` (nombre) que devuelve el backend.

/** Opciones de comportamiento fiscal para selects. */
export const OPCIONES_COMPORTAMIENTO_FISCAL: ReadonlyArray<{
  value: number;
  label: string;
}> = [
  { value: ComportamientoFiscal.MostradorInmediato, label: 'Mostrador inmediato' },
  { value: ComportamientoFiscal.ConAnticipo, label: 'Con anticipo' },
  { value: ComportamientoFiscal.ExportacionConCce, label: 'Exportación (CCE)' },
  {
    value: ComportamientoFiscal.TrasladoConCartaPorte,
    label: 'Traslado (Carta Porte)',
  },
  { value: ComportamientoFiscal.VentaActivoFijo, label: 'Venta de activo fijo' },
  { value: ComportamientoFiscal.Administrativa, label: 'Administrativa' },
];

/** Etiqueta de comportamiento fiscal por NOMBRE de enum. */
export const ETIQUETA_COMPORTAMIENTO_FISCAL: Record<string, string> = {
  MostradorInmediato: 'Mostrador inmediato',
  ConAnticipo: 'Con anticipo',
  ExportacionConCce: 'Exportación (CCE)',
  TrasladoConCartaPorte: 'Traslado (Carta Porte)',
  VentaActivoFijo: 'Venta de activo fijo',
  Administrativa: 'Administrativa',
};

/** Etiqueta del tipo de relación CFDI (catálogo SAT c_TipoRelacion). */
export const ETIQUETA_TIPO_RELACION: Record<string, string> = {
  '01': '01 · Nota de crédito',
  '02': '02 · Nota de débito',
  '03': '03 · Devolución de mercancía',
  '04': '04 · Sustitución de CFDI previo',
  '05': '05 · Traslado de mercancía',
  '06': '06 · Factura por traslados previos',
  '07': '07 · Aplicación de anticipo',
};

/** Etiqueta del motivo de una nota de crédito ([Decisión 13-K]). */
export const ETIQUETA_MOTIVO_NC: Record<string, string> = {
  Amortizacion: 'Aplicación de anticipo',
  Bonificacion: 'Bonificación',
  Devolucion: 'Devolución',
  // RANURA-PR2: NC automática del descuento "ranura" del pedido A+W —
  // etiquetada distinto para distinguirla de las NC comerciales reales.
  Ranura: 'Ranura (descuento de pedido)',
};

/** Etiqueta del estado de timbrado (FSM del comprobante). */
export const ETIQUETA_ESTADO_TIMBRADO: Record<string, string> = {
  Borrador: 'Borrador',
  PendientePedimento: 'Pendiente de pedimento',
  TimbradoEnProceso: 'Timbrado en proceso',
  Timbrado: 'Timbrado',
  TimbradoFallido: 'Timbrado fallido',
  CancelacionPendiente: 'Cancelación pendiente',
  Cancelado: 'Cancelado',
  Descartada: 'Descartada',
};

/** Devuelve la etiqueta del estado de timbrado por su valor numérico. */
export function etiquetaEstadoTimbrado(valor: EstadoTimbrado): string {
  const nombre = Object.entries(EstadoTimbrado).find(
    ([, v]) => v === valor,
  )?.[0];
  return nombre ? (ETIQUETA_ESTADO_TIMBRADO[nombre] ?? nombre) : String(valor);
}

/** Opciones de estado de pedido para el filtro de la bandeja. */
export const OPCIONES_ESTADO_PEDIDO: ReadonlyArray<{
  value: number;
  label: string;
}> = [
  { value: EstadoPedidoFacturable.Importado, label: 'Importado' },
  { value: EstadoPedidoFacturable.Bloqueado, label: 'En facturación' },
  { value: EstadoPedidoFacturable.Facturado, label: 'Facturado' },
  { value: EstadoPedidoFacturable.Cancelado, label: 'Cancelado' },
  { value: EstadoPedidoFacturable.Excepcion, label: 'Excepción' },
];

/** Opciones de origen para el filtro de la bandeja. */
export const OPCIONES_ORIGEN_PEDIDO: ReadonlyArray<{
  value: number;
  label: string;
}> = [
  { value: OrigenPedido.Aw, label: 'A+W' },
  { value: OrigenPedido.PlantaPintura, label: 'Planta Pintura' },
  { value: OrigenPedido.Manual, label: 'Manual' },
];
