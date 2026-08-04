/**
 * Tipos del árbol de trazabilidad cross-módulo (UF7-PR2, FOC6).
 * Mirror de los DTOs backend (<c>NodoArbolDocumento</c>,
 * <c>TipoDocumentoTrazabilidad</c>) — viven en
 * <c>components/erp/trazabilidad</c> en lugar de en una feature
 * porque son consumidos cross-módulo.
 */

export const TipoDocumentoTrazabilidad = {
  // 0–4: Compras (mirror de Compras.Domain.TipoDocumentoTrazabilidad).
  Requisicion: 0,
  OrdenCompra: 1,
  Recepcion: 2,
  FacturaProveedor: 3,
  PagoProveedor: 4,
  // 5–10: Facturación (mirror de TipoNodoTrazabilidadFacturacion, doc 13 §4.3).
  PedidoFacturable: 5,
  FacturaVenta: 6,
  FacturaAnticipo: 7,
  NotaCredito: 8,
  ReciboPago: 9,
  CartaPorte: 10,
} as const satisfies Record<string, number>;

export type TipoDocumentoTrazabilidad =
  (typeof TipoDocumentoTrazabilidad)[keyof typeof TipoDocumentoTrazabilidad];

/**
 * Nodo del árbol — recursivo. Mirror de <c>NodoArbolDocumento</c> backend.
 */
export interface NodoArbolDocumento {
  tipoDocumento: TipoDocumentoTrazabilidad;
  id: string;
  folio: string;
  estado: string;
  /** ISO 8601 UTC. */
  fecha: string;
  ascendentes: NodoArbolDocumento[];
  descendentes: NodoArbolDocumento[];
}

/**
 * Resuelve el label humano del tipo de documento. Cross-módulo: cuando
 * CxP/Recepción/Tesorería sumen sus tipos, agregar acá (los enums ya
 * están reservados).
 */
export function tipoDocumentoLabel(tipo: TipoDocumentoTrazabilidad): string {
  switch (tipo) {
    case TipoDocumentoTrazabilidad.Requisicion:
      return 'Requisición';
    case TipoDocumentoTrazabilidad.OrdenCompra:
      return 'Orden de compra';
    case TipoDocumentoTrazabilidad.Recepcion:
      return 'Recepción';
    case TipoDocumentoTrazabilidad.FacturaProveedor:
      return 'Factura de proveedor';
    case TipoDocumentoTrazabilidad.PagoProveedor:
      return 'Pago a proveedor';
    case TipoDocumentoTrazabilidad.PedidoFacturable:
      return 'Pedido facturable';
    case TipoDocumentoTrazabilidad.FacturaVenta:
      return 'Factura de venta';
    case TipoDocumentoTrazabilidad.FacturaAnticipo:
      return 'Factura de anticipo';
    case TipoDocumentoTrazabilidad.NotaCredito:
      return 'Nota de crédito';
    case TipoDocumentoTrazabilidad.ReciboPago:
      return 'Recibo de pago (REPP)';
    case TipoDocumentoTrazabilidad.CartaPorte:
      return 'Carta porte';
    default:
      return 'Documento';
  }
}

/**
 * Path interno (TanStack Router) del detalle de cada tipo de documento.
 * Cross-módulo: cada módulo dueño es responsable de mantener su path
 * actualizado aquí. <c>null</c> = sin pantalla de detalle aún.
 */
export function tipoDocumentoDetalleRoute(
  tipo: TipoDocumentoTrazabilidad,
  id: string,
): { to: string; params: Record<string, string> } | null {
  switch (tipo) {
    case TipoDocumentoTrazabilidad.Requisicion:
      return { to: '/compras/requisiciones/$id', params: { id } };
    case TipoDocumentoTrazabilidad.OrdenCompra:
      return { to: '/compras/ordenes/$id', params: { id } };
    // Facturación (doc 13 §6.3).
    case TipoDocumentoTrazabilidad.PedidoFacturable:
      return { to: '/facturacion/pedidos/$id', params: { id } };
    case TipoDocumentoTrazabilidad.FacturaVenta:
      return { to: '/facturacion/facturas/$id', params: { id } };
    case TipoDocumentoTrazabilidad.FacturaAnticipo:
      return { to: '/facturacion/anticipos/facturas/$id', params: { id } };
    case TipoDocumentoTrazabilidad.ReciboPago:
      return { to: '/facturacion/repp/$id', params: { id } };
    case TipoDocumentoTrazabilidad.CartaPorte:
      return { to: '/facturacion/carta-porte/$id', params: { id } };
    // NC sin página propia (doc 13 §1 no-alcance) → chip informativo.
    // Recepción/FacturaProveedor/PagoProveedor: cuando el módulo
    // correspondiente exista, agregar la ruta aquí.
    default:
      return null;
  }
}
