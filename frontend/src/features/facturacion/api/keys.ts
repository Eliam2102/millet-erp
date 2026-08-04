/**
 * Query keys de TanStack Query para el módulo Facturación. Mismo shape
 * que <c>cxpKeys</c> / <c>comprasKeys</c> / <c>almacenKeys</c>:
 * <c>['facturacion', recurso, acción, ...filtros]</c>. El namespace
 * permite invalidación masiva al cambiar de empresa.
 *
 * <para>FE-F0 declara el andamio (familia raíz + las dos bandejas que el
 * walking skeleton consume en FE-F1). Las demás familias se agregan en la
 * fase FE que las usa.</para>
 */

export interface ListarPedidosFiltros {
  estado?: number;
  origen?: number;
  canal?: number;
  sucursalId?: string;
  requierePedimento?: boolean;
  q?: string;
  offset?: number;
  limit?: number;
  /** Bucket "Sin asignar" de la Capa A (solo caja.leer-todas, CAJAS-PR6). */
  alcance?: 'sin-asignar';
}

export interface ListarFacturasFiltros {
  estado?: number;
  clienteId?: string;
  canal?: number;
  serie?: string;
  q?: string;
  offset?: number;
  limit?: number;
  /** Bucket "Sin asignar" de la Capa A (solo caja.leer-todas, CAJAS-PR6). */
  alcance?: 'sin-asignar';
}

export const facturacionKeys = {
  all: ['facturacion'] as const,

  // Pedidos facturables (FE-F1-PR1)
  pedidos: () => [...facturacionKeys.all, 'pedidos'] as const,
  pedidosList: (filtros: ListarPedidosFiltros) =>
    [...facturacionKeys.pedidos(), 'list', filtros] as const,
  pedidoById: (id: string) =>
    [...facturacionKeys.pedidos(), 'byId', id] as const,
  pedidoComprobantes: (id: string) =>
    [...facturacionKeys.pedidos(), 'byId', id, 'comprobantes'] as const,
  pedidosExcepciones: () =>
    [...facturacionKeys.pedidos(), 'excepciones'] as const,
  pedidosExcepcionesList: (soloPendientes: boolean) =>
    [...facturacionKeys.pedidosExcepciones(), 'list', { soloPendientes }] as const,

  // Defaults del emisor para el form de emisión (FAC-UX-PR3)
  emisorDefaults: () => [...facturacionKeys.all, 'emisor-defaults'] as const,

  // Lookups de catálogo para pickers de emisión (FAC-UX-PR4)
  clientesLookup: (filtros: Record<string, unknown>) =>
    [...facturacionKeys.all, 'catalogos', 'clientes', filtros] as const,
  productosAwLookup: (filtros: Record<string, unknown>) =>
    [...facturacionKeys.all, 'catalogos', 'productos-aw', filtros] as const,
  // Catálogo administrable de canales de venta (FAC-ING-PR3). Las
  // mutaciones del CRUD admin lo invalidan con esta misma clave.
  canalesVentaLookup: () =>
    [...facturacionKeys.all, 'catalogos', 'canales-venta'] as const,

  // Facturas / comprobantes emitidos (FE-F1-PR1 + FE-F2-PR1)
  facturas: () => [...facturacionKeys.all, 'facturas'] as const,
  facturasList: (filtros: ListarFacturasFiltros) =>
    [...facturacionKeys.facturas(), 'list', filtros] as const,
  facturaById: (id: string) =>
    [...facturacionKeys.facturas(), 'byId', id] as const,
  facturaEnvios: (id: string) =>
    [...facturacionKeys.facturas(), 'byId', id, 'envios'] as const,
  facturaCancelacion: (id: string) =>
    [...facturacionKeys.facturas(), 'byId', id, 'cancelacion'] as const,
  // Bitácora de intentos de timbrado ([Decisión 01-G] G4, F13-PR2).
  comprobanteIntentos: (id: string) =>
    [...facturacionKeys.facturas(), 'byId', id, 'intentos-timbrado'] as const,
  // Árbol de trazabilidad documento-céntrico (ANT-PR3, doc 13 §4.3).
  arbolDocumentos: (raiz: string, id: string) =>
    [...facturacionKeys.all, 'arbol-documentos', raiz, id] as const,

  // Anticipos (FE-F4)
  anticipos: () => [...facturacionKeys.all, 'anticipos'] as const,
  anticiposControl: (filtros: Record<string, unknown>) =>
    [...facturacionKeys.anticipos(), 'control', filtros] as const,
  anticiposControlCliente: (clienteId: string) =>
    [...facturacionKeys.anticipos(), 'control', 'cliente', clienteId] as const,
  // Bandeja + detalle de facturas de anticipo (ANT-PR2, doc 13).
  anticiposFacturasList: (filtros: Record<string, unknown>) =>
    [...facturacionKeys.anticipos(), 'facturas', 'list', filtros] as const,
  anticipoFacturaById: (id: string) =>
    [...facturacionKeys.anticipos(), 'facturas', 'byId', id] as const,

  // REPP (FE-F6)
  repp: () => [...facturacionKeys.all, 'repp'] as const,
  reppList: (filtros: Record<string, unknown>) =>
    [...facturacionKeys.repp(), 'list', filtros] as const,
  reppById: (id: string) => [...facturacionKeys.repp(), 'byId', id] as const,
  reppFacturasCobrables: (receptorRfc: string | null) =>
    [...facturacionKeys.repp(), 'facturas-cobrables', receptorRfc] as const,

  // Carta Porte (FE-F8)
  cartaPorte: () => [...facturacionKeys.all, 'carta-porte'] as const,
  cartaPorteList: (filtros: Record<string, unknown>) =>
    [...facturacionKeys.cartaPorte(), 'list', filtros] as const,
  cartaPorteById: (id: string) =>
    [...facturacionKeys.cartaPorte(), 'byId', id] as const,
  // Catálogos administrables de Carta Porte (vehículos/operadores).
  // Las mutaciones del CRUD admin invalidan la familia raíz.
  cartaPorteVehiculos: () =>
    [...facturacionKeys.cartaPorte(), 'vehiculos'] as const,
  cartaPorteVehiculosList: (incluirInactivos: boolean) =>
    [...facturacionKeys.cartaPorteVehiculos(), 'list', { incluirInactivos }] as const,
  cartaPorteOperadores: () =>
    [...facturacionKeys.cartaPorte(), 'operadores'] as const,
  cartaPorteOperadoresList: (incluirInactivos: boolean) =>
    [...facturacionKeys.cartaPorteOperadores(), 'list', { incluirInactivos }] as const,

  // Reportes (FE-F9)
  reportes: () => [...facturacionKeys.all, 'reportes'] as const,
  reporteLiquidacionCaja: (filtros: Record<string, unknown>) =>
    [...facturacionKeys.reportes(), 'liquidacion-caja', filtros] as const,
  reporteEstadosAnticipos: (filtros: Record<string, unknown>) =>
    [...facturacionKeys.reportes(), 'estados-anticipos', filtros] as const,

  // Activos (FE-F9)
  activosAutorizaciones: (estado: string | null) =>
    [...facturacionKeys.all, 'activos', 'autorizaciones', { estado }] as const,

  // Cajas (CAJAS-PR5)
  cajas: () => [...facturacionKeys.all, 'cajas'] as const,
  cajasList: (filtros: { soloActivas?: boolean }) =>
    [...facturacionKeys.cajas(), 'list', filtros] as const,
  cajaById: (id: string) => [...facturacionKeys.cajas(), 'byId', id] as const,
  usuarioAlcances: () => [...facturacionKeys.cajas(), 'usuario-alcances'] as const,
  usuarioAlcancesList: (usuarioId: string | null) =>
    [...facturacionKeys.usuarioAlcances(), 'list', { usuarioId }] as const,

  // Sesiones de caja + cobros (CAJAS-PR6)
  sesiones: () => [...facturacionKeys.cajas(), 'sesiones'] as const,
  sesionActual: () => [...facturacionKeys.sesiones(), 'actual'] as const,
  sesionById: (id: string) => [...facturacionKeys.sesiones(), 'byId', id] as const,
  sesionesList: (cajaId: string) =>
    [...facturacionKeys.sesiones(), 'list', { cajaId }] as const,
  cobros: (sesionId: string) =>
    [...facturacionKeys.cajas(), 'cobros', { sesionId }] as const,

  // Liquidación de ruta + ajustes (CAJAS-PR7)
  cobrables: (search: string | null) =>
    [...facturacionKeys.cajas(), 'cobrables', { search }] as const,
  ajustesCaja: (cajaId: string, incluirAplicados: boolean) =>
    [...facturacionKeys.cajas(), 'ajustes', { cajaId, incluirAplicados }] as const,
};
