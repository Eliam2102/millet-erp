import {
  ComportamientoFiscal,
  EstadoPedidoFacturable,
  EstadoTimbrado,
  OrigenPedido,
  SubtipoVentaDirecta,
  type ClasePedidoManualNombre,
  type ClasificacionPedidoLectura,
  type SubtipoVentaDirectaNombre,
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

/**
 * Opciones de comportamiento fiscal para selects de documentos nuevos.
 * «Traslado (Carta Porte)» ya no se ofrece: la Carta Porte es un complemento
 * con su propia pantalla, no un comportamiento de la venta (86e3wzcn4).
 */
export const OPCIONES_COMPORTAMIENTO_FISCAL: ReadonlyArray<{
  value: number;
  label: string;
}> = [
  { value: ComportamientoFiscal.MostradorInmediato, label: 'Mostrador inmediato' },
  { value: ComportamientoFiscal.ConAnticipo, label: 'Con anticipo' },
  { value: ComportamientoFiscal.ExportacionConCce, label: 'Exportación (CCE)' },
  { value: ComportamientoFiscal.VentaActivoFijo, label: 'Venta de activo fijo' },
  { value: ComportamientoFiscal.Administrativa, label: 'Administrativa' },
];

/**
 * Opciones del select según el valor que ya trae el documento: un pedido o
 * factura guardado con «Traslado (Carta Porte)» la conserva para no cambiarle
 * el dato en silencio; los nuevos no la ven.
 */
export function opcionesComportamientoFiscal(
  actual?: number | null,
): ReadonlyArray<{ value: number; label: string }> {
  return actual === ComportamientoFiscal.TrasladoConCartaPorte
    ? [
        ...OPCIONES_COMPORTAMIENTO_FISCAL,
        { value: ComportamientoFiscal.TrasladoConCartaPorte, label: 'Traslado (Carta Porte)' },
      ]
    : OPCIONES_COMPORTAMIENTO_FISCAL;
}

/** Aviso junto al selector: la Carta Porte tiene su propia pantalla. */
export const AVISO_CARTA_PORTE = 'La Carta Porte se emite desde Facturación › Carta Porte.';

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

// ─── F6.1: clase de pedido manual ─────────────────────────────────────

/** Etiqueta del subtipo de venta directa por nombre (lecturas). */
export const ETIQUETA_SUBTIPO_VENTA_DIRECTA: Record<SubtipoVentaDirectaNombre, string> = {
  Activo: 'Activo',
  Almacenable: 'Almacenable',
  Servicio: 'Servicio',
};

/** Opciones del subtipo de venta directa (captura, valor numérico). */
export const OPCIONES_SUBTIPO_VENTA_DIRECTA: ReadonlyArray<{
  value: SubtipoVentaDirecta;
  label: string;
  descripcion: string;
}> = [
  {
    value: SubtipoVentaDirecta.Activo,
    label: 'Activo',
    descripcion: 'Venta de un activo fijo de la empresa.',
  },
  {
    value: SubtipoVentaDirecta.Almacenable,
    label: 'Almacenable',
    descripcion: 'Producto de inventario que no pasa por producción.',
  },
  {
    value: SubtipoVentaDirecta.Servicio,
    label: 'Servicio',
    descripcion: 'Servicio sin entrega de mercancía.',
  },
];

/** Opciones del filtro «Clase» de las bandejas (valor = nombre del enum). */
export const OPCIONES_CLASE_PEDIDO: ReadonlyArray<{
  value: ClasePedidoManualNombre;
  label: string;
}> = [
  { value: 'VentaDirecta', label: 'Venta directa' },
  { value: 'Emergencia', label: 'Emergencia' },
];

/**
 * Texto del badge de clase: «Venta directa · Activo», «Emergencia» o
 * null si el registro no viene de un pedido manual clasificado.
 */
export function etiquetaClasePedido(
  c: ClasificacionPedidoLectura,
): string | null {
  if (c.esEmergencia === true || c.claseManual === 'Emergencia') {
    return 'Emergencia';
  }
  if (c.claseManual === 'VentaDirecta') {
    const subtipo =
      c.subtipoVentaDirecta != null
        ? ETIQUETA_SUBTIPO_VENTA_DIRECTA[c.subtipoVentaDirecta]
        : null;
    return subtipo ? `Venta directa · ${subtipo}` : 'Venta directa';
  }
  return null;
}
