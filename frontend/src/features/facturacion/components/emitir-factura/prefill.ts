/**
 * Prefill opcional del form de emisión al facturar desde un pedido
 * facturable (FE-F1-PR4 → FAC-UX-PR2/PR3). Liga la factura al pedido
 * (`pedidoFacturableId`, B2) y precarga datos comerciales + fiscales:
 * los del receptor vienen del master `compartido.clientes` vía el
 * detalle del pedido; los del emisor los aporta el propio form con
 * `useEmisorDefaults()`.
 */
export interface EmitirFacturaPrefill {
  embarqueCe?: import('@/features/comercio-exterior/api/types').EmbarqueCe | null;
  /** Ausente al sustituir (FAC-08b): la sustituta hereda el pedido al cancelarse la original. */
  pedidoFacturableId?: string;
  /**
   * FAC-08: factura solo esta parcialidad del pedido. Las partidas del
   * prefill son las de la salida (mismas posiciones y cantidades) y el
   * pedido sigue abierto para las siguientes salidas.
   */
  salidaFacturableId?: string | null;
  /** FAC-08: número de la parcialidad que se factura (solo para mostrar). */
  numeroParcialidad?: number | null;
  /** FAC-08b: factura timbrada que la nueva sustituye (relación 04). */
  sustituye?: { id: string; folio: string; uuid: string };
  tipoCambio?: number | null;
  ordenCompraCliente?: string | null;
  proforma?: string | null;
  /** Origen del pedido ("Aw", "PlantaPintura", "Manual"). */
  origen?: string;
  /**
   * F6.1: clase del pedido manual. Venta directa y emergencia se timbran
   * con la serie de Venta directa de la sucursal; null fuera de manuales.
   */
  claseManual?: import('@/features/facturacion/api/types').ClasePedidoManualNombre | null;
  /** F6.1: subtipo de la venta directa; Activo fija «Venta de activo fijo». */
  subtipoVentaDirecta?: import('@/features/facturacion/api/types').SubtipoVentaDirectaNombre | null;
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
  /**
   * Receptor extranjero (CCE) del catálogo del cliente, igual que el selector
   * de la factura manual: NumRegIdTrib, país de residencia y domicilio.
   */
  cceReceptorNumRegIdTrib?: string | null;
  cceReceptorPaisResidencia?: string | null;
  cceReceptorDomicilioCalle?: string | null;
  cceReceptorDomicilioEstado?: string | null;
  cceReceptorDomicilioCodigoPostal?: string | null;
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
  condicionesPago?: string | null;
  referenciaCliente?: string | null;
  observaciones?: string | null;
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
    // B12: datos de aduana y peso del artículo para la línea de exportación (CCE).
    fraccionArancelaria?: string | null;
    unidadAduana?: string | null;
    pesoUnitarioKg?: number | null;
  }>;
}
