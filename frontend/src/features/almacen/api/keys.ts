/**
 * Query keys de TanStack Query para el módulo Almacén. Mismo shape
 * que <c>comprasKeys</c>: <c>['almacen', recurso, acción, ...filtros]</c>.
 * El namespace permite invalidación masiva al cambiar empresa.
 */

export interface ListarAlmacenesFiltros {
  estatus?: number;
  sucursalId?: string;
  q?: string;
  offset?: number;
  limit?: number;
}

export interface ListarSubAlmacenesFiltros {
  almacenId?: string;
  tipo?: number;
  estatus?: number;
  q?: string;
  offset?: number;
  limit?: number;
}

export interface ListarRecepcionesFiltros {
  estado?: number;
  subAlmacenId?: string;
  ordenCompraId?: string;
  desde?: string; // YYYY-MM-DD
  hasta?: string;
  offset?: number;
  limit?: number;
  proveedorId?: string;
  /** Búsqueda por folio, server-side. */
  q?: string;
}

export interface ListarSalidasFiltros {
  estado?: number;
  subAlmacenId?: string;
  rqId?: string;
  personaDestinatariaId?: string;
  desde?: string;
  hasta?: string;
  soloVales?: boolean;
  /**
   * Solo vales que aún NO han sido regularizados con RQ posterior
   * (A14, SLA 48h). El backend implica <c>soloVales=true</c> aunque
   * el caller no lo pase.
   */
  noRegularizados?: boolean;
  soloVencidos?: boolean;
  soloPorVencer?: boolean;
  offset?: number;
  limit?: number;
  /** Búsqueda por folio, server-side. */
  q?: string;
}

export interface ListarDevolucionesProveedorFiltros {
  estado?: number;
  proveedorId?: string;
  soloPendientesNcFiscal?: boolean;
  offset?: number;
  limit?: number;
  /** Búsqueda por folio de la salida, server-side. */
  q?: string;
}

export interface ListarConteosFiltros {
  estado?: number;
  tipo?: number;
  subAlmacenId?: string;
  offset?: number;
  limit?: number;
  /** Búsqueda por familia (el conteo no tiene folio), server-side. */
  q?: string;
}

export interface ListarSaldosFiltros {
  subAlmacenId?: string;
  articuloId?: string;
  soloConStock?: boolean;
  offset?: number;
  limit?: number;
}

export interface ListarReordenFiltros {
  articuloId?: string;
  /** Backend enum NivelReorden: 0 Sucursal, 1 Almacén. */
  nivel?: number;
  entidadId?: string;
  /** Backend enum EstatusCatalogo: 0 Activo, 1 Inactivo, 2 EnRevisión. */
  estatus?: number;
  offset?: number;
  limit?: number;
}

export interface ListarUbicacionesFiltros {
  subAlmacenId?: string;
  /** Backend enum EstatusCatalogo: 0 Activo, 1 Inactivo, 2 EnRevisión. */
  estatus?: number;
  q?: string;
  offset?: number;
  limit?: number;
}

export interface ListarAsignacionesFiltros {
  ubicacionId?: string;
  articuloId?: string;
  /** Backend enum EstatusCatalogo: 0 Activo, 1 Inactivo, 2 EnRevisión. */
  estatus?: number;
  offset?: number;
  limit?: number;
}

/** Params de la consulta jerárquica (PR6): "hijos del nodo X". */
export interface HijosJerarquiaParams {
  nodoTipo: 'raiz' | 'sucursal' | 'almacen' | 'subAlmacen' | 'ubicacion';
  nodoId?: string;
  /** Presente = modo artículo (filtra todas las ramas; la ubicación es hoja). */
  articuloId?: string;
  incluirVacios?: boolean;
}

export interface AlfakHistorialFiltros {
  desde: string; // YYYY-MM-DD
  hasta: string;
  subAlmacenId?: string;
  articuloId?: string;
}

export interface TrazabilidadMpFiltros {
  articuloId: string;
  desde: string; // YYYY-MM-DD
  hasta: string;
  lote?: string;
  subAlmacenId?: string;
}

export interface ConsumoInsumosLineaFiltros {
  desde: string; // YYYY-MM-DD
  hasta: string;
  sucursalId?: string;
  centroCostoId?: string;
  articuloId?: string;
}

export interface KardexFiltros {
  articuloId: string;
  desde: string; // YYYY-MM-DD
  hasta: string;
  subAlmacenId?: string;
  ubicacionId?: string;
}

export interface MpCnkFiltros {
  subAlmacenId?: string;
}

export const almacenKeys = {
  all: ['almacen'] as const,

  // Almacenes (catálogo)
  almacenes: () => [...almacenKeys.all, 'almacenes'] as const,
  almacenesList: (filtros: ListarAlmacenesFiltros) =>
    [...almacenKeys.almacenes(), 'list', filtros] as const,
  almacenById: (id: string) =>
    [...almacenKeys.almacenes(), 'byId', id] as const,

  // Sub-almacenes
  subAlmacenes: () => [...almacenKeys.all, 'sub-almacenes'] as const,
  subAlmacenesList: (filtros: ListarSubAlmacenesFiltros) =>
    [...almacenKeys.subAlmacenes(), 'list', filtros] as const,

  // Material en revisión (ALM-04)
  revision: (f: { soloPendientes: boolean; q?: string }) =>
    [...almacenKeys.all, 'revision', f] as const,

  // Desperdicio de mesa y merma de proceso (A4.4)
  mermas: (f: { estado?: string; tipo?: string }) => [...almacenKeys.all, 'mermas', f] as const,

  // Normas de consumo de componentes y su bandeja de revisión (A4.8)
  normasConsumo: () => [...almacenKeys.all, 'normas-consumo'] as const,
  // Tambores por línea de producción (M-172)
  tamboresLinea: () => [...almacenKeys.all, 'tambores-linea'] as const,
  consumosRevision: () => [...almacenKeys.all, 'consumos-revision'] as const,
  mensajesAppMarcados: () => [...almacenKeys.all, 'mensajes-app-marcados'] as const,
  origenCapturaMp: () => [...almacenKeys.all, 'origen-captura-mp'] as const,

  // Salida por rotura en almacén (ALM-03)
  salidasRotura: (f: { estado?: string }) => [...almacenKeys.all, 'salidas-rotura', f] as const,
  motivosRotura: () => [...almacenKeys.all, 'salidas-rotura', 'motivos'] as const,
  dimensionesRotura: (ubicacionId: string, fecha: string) =>
    [...almacenKeys.all, 'salidas-rotura', 'dimensiones', ubicacionId, fecha] as const,
  // M-450: si la cuenta del ajuste por conteo exige área o equipo (regla C-03).
  dimensionesConteo: (conteoId: string) => [...almacenKeys.all, 'conteos', conteoId, 'dimensiones'] as const,

  // Conciliación diaria contra A+W (A4.6)
  conciliacion: {
    existencia: (almacenId: string) => [...almacenKeys.all, 'conciliacion', 'existencia', almacenId] as const,
    diferencias: () => [...almacenKeys.all, 'conciliacion', 'diferencias'] as const,
    corridas: () => [...almacenKeys.all, 'conciliacion', 'corridas'] as const,
  },

  // Recepciones
  recepciones: () => [...almacenKeys.all, 'recepciones'] as const,
  recepcionesList: (filtros: ListarRecepcionesFiltros) =>
    [...almacenKeys.recepciones(), 'list', filtros] as const,
  recepcionById: (id: string) =>
    [...almacenKeys.recepciones(), 'byId', id] as const,
  recepcionRecibibles: (ocId: string) =>
    ['almacen', 'recepcion-recibibles', ocId] as const,
  recepcionCfdis: (ocId: string) => ['almacen', 'recepcion-cfdis', ocId] as const,

  // Salidas
  salidas: () => [...almacenKeys.all, 'salidas'] as const,
  salidasList: (filtros: ListarSalidasFiltros) =>
    [...almacenKeys.salidas(), 'list', filtros] as const,
  salidaById: (id: string) =>
    [...almacenKeys.salidas(), 'byId', id] as const,

  // Devoluciones a proveedor (8.B). Las internas no tienen bandeja
  // propia — sus movimientos quedan en `movimientos` y se ven via
  // la bandeja de recepciones/salidas según el tipo.
  devolucionesProveedor: () =>
    [...almacenKeys.all, 'devoluciones-proveedor'] as const,
  devolucionesProveedorList: (filtros: ListarDevolucionesProveedorFiltros) =>
    [...almacenKeys.devolucionesProveedor(), 'list', filtros] as const,
  devolucionProveedorById: (id: string) =>
    [...almacenKeys.devolucionesProveedor(), 'byId', id] as const,

  // Conteos / inventario físico
  conteos: () => [...almacenKeys.all, 'conteos'] as const,
  conteosList: (filtros: ListarConteosFiltros) =>
    [...almacenKeys.conteos(), 'list', filtros] as const,
  conteoVistaPrevia: (subAlmacenId: string | null, familia: string | null) =>
    [...almacenKeys.conteos(), 'vista-previa', subAlmacenId, familia] as const,
  conteoById: (id: string) => [...almacenKeys.conteos(), 'byId', id] as const,
  lineasParaCapturar: (conteoId: string) =>
    [...almacenKeys.conteoById(conteoId), 'lineas-para-capturar'] as const,
  lineasComparacion: (conteoId: string) =>
    [...almacenKeys.conteoById(conteoId), 'comparacion'] as const,

  // Saldos materializados
  saldos: () => [...almacenKeys.all, 'saldos'] as const,
  saldosList: (filtros: ListarSaldosFiltros) =>
    [...almacenKeys.saldos(), 'list', filtros] as const,
  saldosTotales: (filtros: ListarSaldosFiltros) =>
    [...almacenKeys.saldos(), 'totales', filtros] as const,
  // C7.2b: saldo por ubicación de un artículo (selector de bin en salidas)
  saldosPorUbicacion: (articuloId: string, subAlmacenId: string, varianteId = '') =>
    [...almacenKeys.saldos(), 'por-ubicacion', articuloId, subAlmacenId, varianteId] as const,
  // PR6: hijos de un nodo de la jerarquía (una key por nodo+filtros → cada
  // expansión del árbol se cachea de forma independiente)
  saldosJerarquia: (params: HijosJerarquiaParams) =>
    [...almacenKeys.saldos(), 'jerarquia', params] as const,

  // Cierre de mes
  cierrePeriodos: () => [...almacenKeys.all, 'cierre-mes', 'periodos'] as const,
  cierreRevision: (anio: number, mes: number) =>
    [...almacenKeys.all, 'cierre-mes', 'revision-previa', anio, mes] as const,
  cierreActas: () => [...almacenKeys.all, 'cierre-mes', 'actas'] as const,
  avisos: () => [...almacenKeys.all, 'avisos'] as const,

  // Reabasto / reorden (config N1-N2)
  reorden: () => [...almacenKeys.all, 'reorden'] as const,
  reordenList: (filtros: ListarReordenFiltros) =>
    [...almacenKeys.reorden(), 'list', filtros] as const,
  reordenById: (id: string) => [...almacenKeys.reorden(), 'byId', id] as const,

  // Configuración del módulo (interruptor de reabasto automático)
  settings: () => [...almacenKeys.all, 'settings'] as const,

  // Ubicaciones (N4) — catálogo de lectura para el UbicacionSelector
  ubicaciones: () => [...almacenKeys.all, 'ubicaciones'] as const,
  ubicacionesList: (filtros: ListarUbicacionesFiltros) =>
    [...almacenKeys.ubicaciones(), 'list', filtros] as const,

  // Asignaciones artículo↔ubicación (N4, "Ubicación de artículos")
  asignaciones: () => [...almacenKeys.all, 'asignaciones'] as const,
  asignacionesList: (filtros: ListarAsignacionesFiltros) =>
    [...almacenKeys.asignaciones(), 'list', filtros] as const,

  // Reportes
  reportes: () => [...almacenKeys.all, 'reportes'] as const,
  reporteAlfak: (filtros: AlfakHistorialFiltros) =>
    [...almacenKeys.reportes(), 'alfak', filtros] as const,
  reporteTrazabilidadMp: (filtros: TrazabilidadMpFiltros) =>
    [...almacenKeys.reportes(), 'trazabilidad-mp', filtros] as const,
  reporteConsumoInsumosLinea: (filtros: ConsumoInsumosLineaFiltros) =>
    [...almacenKeys.reportes(), 'consumo-insumos-linea', filtros] as const,
  reporteKardex: (filtros: KardexFiltros) =>
    [...almacenKeys.reportes(), 'kardex', filtros] as const,
  reporteCuadreInventario: (anio: number, mes: number) =>
    [...almacenKeys.reportes(), 'cuadre-inventario', anio, mes] as const,
  reporteMpCnk: (filtros: MpCnkFiltros) =>
    [...almacenKeys.reportes(), 'mp-cnk', filtros] as const,
};
