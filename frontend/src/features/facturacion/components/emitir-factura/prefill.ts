/**
 * Prefill opcional del form de emisión al facturar desde un pedido
 * facturable (FE-F1-PR4 → FAC-UX-PR2/PR3). Liga la factura al pedido
 * (`pedidoFacturableId`, B2) y precarga datos comerciales + fiscales:
 * los del receptor vienen del master `compartido.clientes` vía el
 * detalle del pedido; los del emisor los aporta el propio form con
 * `useEmisorDefaults()`.
 */
export interface EmitirFacturaPrefill {
  pedidoFacturableId: string;
  sucursalId?: string;
  /** Cliente del pedido — habilita el picker de anticipos (FAC-UX-PR4). */
  clienteId?: string;
  receptorNombre?: string;
  // Datos fiscales del receptor (master de clientes, FAC-UX-PR3).
  // Undefined/null = incompletos en el master (gap G12) → captura manual.
  receptorRfc?: string | null;
  receptorRegimenFiscal?: string | null;
  receptorCodigoPostal?: string | null;
  receptorUsoCfdi?: string | null;
  metodoPago?: string | null;
  formaPago?: string | null;
  /** True si el cliente no está en el master o le faltan datos fiscales
   * (banner G12: completarlos en Datos Maestros → Clientes). */
  datosFiscalesIncompletos?: boolean;
  /** Id del catálogo `compartido.canales_venta` (FAC-ING-PR3). */
  canalVenta?: number;
  /** Nombre del canal del pedido — conserva la opción en el selector si
   * el canal fue desactivado después de capturar el pedido. */
  canalVentaNombre?: string;
  comportamientoFiscal?: number;
  moneda?: string;
  obraNombre?: string | null;
  lineas?: Array<{
    productoId: string | null;
    claveProdServSat: string;
    descripcion: string;
    claveUnidadSat: string;
    cantidad: number;
    valorUnitario: number;
    descuento: number;
    requierePedimento: boolean;
    // Del master producto_aw (FAC-UX-PR3); null = usar fallback del form.
    objetoImp?: string | null;
    tasaIvaTraslado?: number | null;
    tasaRetencionIva?: number | null;
    tasaRetencionIsr?: number | null;
  }>;
}
