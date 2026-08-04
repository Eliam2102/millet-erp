/**
 * Query keys de TanStack Query para el módulo CxP. Mismo shape que
 * <c>comprasKeys</c> y <c>almacenKeys</c>:
 * <c>['cxp', recurso, acción, ...filtros]</c>. El namespace permite
 * invalidación masiva al cambiar empresa.
 */

export interface ListarCfdisFiltros {
  estado?: number;
  tipo?: number;
  rfcEmisor?: string;
  offset?: number;
  limit?: number;
}

export interface ListarFacturasFiltros {
  estado?: number;
  proveedorId?: string;
  sucursalId?: string;
  offset?: number;
  limit?: number;
}

export const cxpKeys = {
  all: ['cxp'] as const,

  // CFDIs recibidos (FE-F1-PR1)
  cfdis: () => [...cxpKeys.all, 'cfdis'] as const,
  cfdisList: (filtros: ListarCfdisFiltros) =>
    [...cxpKeys.cfdis(), 'list', filtros] as const,
  cfdiParseado: (id: string) =>
    [...cxpKeys.cfdis(), 'parseado', id] as const,
  cfdiDetalle: (id: string) =>
    [...cxpKeys.cfdis(), 'detalle', id] as const,
  cfdiPdfUrl: (id: string) =>
    [...cxpKeys.cfdis(), 'pdf-url', id] as const,

  // Facturas de proveedor (FE-F2-PR1)
  facturas: () => [...cxpKeys.all, 'facturas'] as const,
  facturasList: (filtros: ListarFacturasFiltros) =>
    [...cxpKeys.facturas(), 'list', filtros] as const,
  facturaById: (id: string) => [...cxpKeys.facturas(), 'byId', id] as const,

  // Revisión por área (FE-F3-PR1)
  enRevision: () => [...cxpKeys.facturas(), 'enRevision'] as const,
  enRevisionList: (dependenciaRevisoraId: string, motivoRevisionId?: string) =>
    [
      ...cxpKeys.enRevision(),
      'list',
      { dependenciaRevisoraId, motivoRevisionId: motivoRevisionId ?? null },
    ] as const,
  evidenciasByFactura: (facturaId: string) =>
    [...cxpKeys.facturaById(facturaId), 'evidencias'] as const,

  // Catálogos
  catalogosMotivosRevision: () =>
    [...cxpKeys.all, 'catalogos', 'motivos-revision'] as const,

  // Notas de crédito (FE-F4-PR1)
  notasCredito: () => [...cxpKeys.all, 'notas-credito'] as const,
  notasCreditoList: (filtros: ListarNotasCreditoFiltros) =>
    [...cxpKeys.notasCredito(), 'list', filtros] as const,
  notaCreditoById: (id: string) =>
    [...cxpKeys.notasCredito(), 'byId', id] as const,

  // Anticipos (FE-F4-PR1)
  anticipos: () => [...cxpKeys.all, 'anticipos'] as const,
  anticiposList: (filtros: ListarAnticiposFiltros) =>
    [...cxpKeys.anticipos(), 'list', filtros] as const,

  // Notas de cargo (FE-F4-PR1)
  notasCargo: () => [...cxpKeys.all, 'notas-cargo'] as const,
  notasCargoList: (filtros: ListarNotasCargoFiltros) =>
    [...cxpKeys.notasCargo(), 'list', filtros] as const,
  notaCargoById: (id: string) =>
    [...cxpKeys.notasCargo(), 'byId', id] as const,

  // Comprobaciones de gastos (FE-F4-PR2)
  comprobaciones: () => [...cxpKeys.all, 'comprobaciones'] as const,
  comprobacionesList: (filtros: ListarComprobacionesFiltros) =>
    [...cxpKeys.comprobaciones(), 'list', filtros] as const,
  comprobacionById: (id: string) =>
    [...cxpKeys.comprobaciones(), 'byId', id] as const,

  // Viáticos (FE-F5-PR1)
  viaticos: () => [...cxpKeys.all, 'viaticos'] as const,
  viaticosList: (filtros: ListarViaticosFiltros) =>
    [...cxpKeys.viaticos(), 'list', filtros] as const,
  viaticoById: (id: string) => [...cxpKeys.viaticos(), 'byId', id] as const,

  // Tarjetas de Crédito (FE-F6-PR1)
  tarjetas: () => [...cxpKeys.all, 'tarjetas'] as const,
  tarjetasList: (filtros: ListarTarjetasFiltros) =>
    [...cxpKeys.tarjetas(), 'list', filtros] as const,
  tarjetaById: (id: string) => [...cxpKeys.tarjetas(), 'byId', id] as const,

  movimientosTc: () => [...cxpKeys.all, 'movimientos-tc'] as const,
  movimientosTcList: (filtros: ListarMovimientosTcFiltros) =>
    [...cxpKeys.movimientosTc(), 'list', filtros] as const,

  // Estados de cuenta TC (FE-F6-PR2)
  estadosCuentaTc: () => [...cxpKeys.all, 'estados-cuenta-tc'] as const,
  estadosCuentaTcList: (filtros: ListarEstadosCuentaTcFiltros) =>
    [...cxpKeys.estadosCuentaTc(), 'list', filtros] as const,
  estadoCuentaTcById: (id: string) =>
    [...cxpKeys.estadosCuentaTc(), 'byId', id] as const,

  // Admin catálogos (cxp-fe/admin-pages)
  aprobadoresLimites: () =>
    [...cxpKeys.all, 'aprobadores-limites'] as const,
  aprobadoresLimitesList: (filtros: ListarAprobadoresLimitesFiltros) =>
    [...cxpKeys.aprobadoresLimites(), 'list', filtros] as const,
  politicasViaticos: () => [...cxpKeys.all, 'politicas-viaticos'] as const,
  politicasViaticosList: (filtros: ListarPoliticasViaticosFiltros) =>
    [...cxpKeys.politicasViaticos(), 'list', filtros] as const,
};

export interface ListarNotasCreditoFiltros {
  estado?: number;
  proveedorId?: string;
  facturaOrigenId?: string;
  offset?: number;
  limit?: number;
}

export interface ListarAnticiposFiltros {
  estado?: number;
  proveedorId?: string;
  ordenCompraId?: string;
  offset?: number;
  limit?: number;
}

export interface ListarNotasCargoFiltros {
  estado?: number;
  proveedorId?: string;
  facturaOrigenId?: string;
  offset?: number;
  limit?: number;
}

export interface ListarComprobacionesFiltros {
  tipo?: number;
  estado?: number;
  sucursalId?: string;
  responsableId?: string;
  offset?: number;
  limit?: number;
}

export interface ListarViaticosFiltros {
  estado?: number;
  empleadoId?: string;
  jefeDirectoId?: string;
  offset?: number;
  limit?: number;
}

export interface ListarTarjetasFiltros {
  estado?: number;
  titularId?: string;
  offset?: number;
  limit?: number;
}

export interface ListarMovimientosTcFiltros {
  tarjetaId?: string;
  usuarioQueUsoId?: string;
  estado?: number;
  tipo?: number;
  fechaDesde?: string;
  fechaHasta?: string;
  offset?: number;
  limit?: number;
}

export interface ListarEstadosCuentaTcFiltros {
  tarjetaId?: string;
  estado?: number;
  offset?: number;
  limit?: number;
}

export interface ListarAprobadoresLimitesFiltros {
  empleadoId?: string;
  tipoGasto?: number;
  soloVigentes?: boolean;
  offset?: number;
  limit?: number;
}

export interface ListarPoliticasViaticosFiltros {
  puestoId?: string;
  tipoDestino?: number;
  offset?: number;
  limit?: number;
}
