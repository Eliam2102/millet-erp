/**
 * Query keys de TanStack Query para el módulo Cuentas por Cobrar. Mismo
 * shape que <c>facturacionKeys</c> / <c>cxpKeys</c> / <c>comprasKeys</c>:
 * <c>['cxc', recurso, acción, ...filtros]</c>. El namespace permite
 * invalidación masiva al cambiar de empresa.
 *
 * <para>CXC-FE-PR1 declara el andamio (familia raíz + líneas de crédito
 * + crédito disponible, que el walking skeleton consume en FE-PR2). Las
 * demás familias (liberaciones, cobranza, cartera, aplicaciones,
 * alertas) se agregan en la fase FE que las usa.</para>
 */

export interface ListarLineasCreditoFiltros {
  clienteId?: string;
  estado?: number;
  moneda?: string;
  q?: string;
  offset?: number;
  limit?: number;
}

export const cxcKeys = {
  all: ['cxc'] as const,

  // Líneas de crédito (CXC-FE-PR1/PR2)
  lineasCredito: () => [...cxcKeys.all, 'lineas-credito'] as const,
  lineasCreditoList: (filtros: ListarLineasCreditoFiltros) =>
    [...cxcKeys.lineasCredito(), 'list', filtros] as const,
  lineaCreditoById: (id: string) =>
    [...cxcKeys.lineasCredito(), 'byId', id] as const,

  // Crédito disponible por cliente (CXC-FE-PR2)
  creditoDisponible: (clienteId: string) =>
    [...cxcKeys.all, 'credito-disponible', clienteId] as const,

  // Lookup de clientes del módulo (CXC-FE-PR2)
  clientesLookup: (filtros: Record<string, unknown>) =>
    [...cxcKeys.all, 'clientes-lookup', filtros] as const,

  // Liberaciones + autorizaciones consumibles (CXC-FE-PR3)
  liberaciones: () => [...cxcKeys.all, 'liberaciones'] as const,
  liberacionesList: (filtros: Record<string, unknown>) =>
    [...cxcKeys.liberaciones(), 'list', filtros] as const,
  liberacionById: (id: string) =>
    [...cxcKeys.liberaciones(), 'byId', id] as const,
  autorizaciones: () => [...cxcKeys.all, 'autorizaciones'] as const,
  autorizacionesList: (filtros: Record<string, unknown>) =>
    [...cxcKeys.autorizaciones(), 'list', filtros] as const,

  // Cobranza (CXC-FE-PR4)
  cobranza: () => [...cxcKeys.all, 'cobranza'] as const,
  cobranzaList: (filtros: Record<string, unknown>) =>
    [...cxcKeys.cobranza(), 'list', filtros] as const,

  // Cartera: reportes ADR-0036 + anticipos del read port (CXC-FE-PR5)
  cartera: () => [...cxcKeys.all, 'cartera'] as const,
  antiguedad: (filtros: Record<string, unknown>) =>
    [...cxcKeys.cartera(), 'antiguedad', filtros] as const,
  estadoCuenta: (clienteId: string) =>
    [...cxcKeys.cartera(), 'estado-cuenta', clienteId] as const,
  anticipos: (clienteId: string) =>
    [...cxcKeys.all, 'anticipos', clienteId] as const,

  // Aplicación de pagos (CXC-FE-PR6)
  propuestas: () => [...cxcKeys.all, 'propuestas-aplicacion'] as const,
  propuestasList: (filtros: Record<string, unknown>) =>
    [...cxcKeys.propuestas(), 'list', filtros] as const,
  propuestaById: (id: string) =>
    [...cxcKeys.propuestas(), 'byId', id] as const,
  facturasAbiertas: (clienteId: string, moneda: string | null) =>
    [...cxcKeys.cartera(), 'facturas-abiertas', { clienteId, moneda }] as const,
  tolerancias: () => [...cxcKeys.propuestas(), 'tolerancias'] as const,

  // Alertas de cartera (CXC-FE-PR7)
  alertas: () => [...cxcKeys.all, 'alertas'] as const,
  alertasList: (filtros: Record<string, unknown>) =>
    [...cxcKeys.alertas(), 'list', filtros] as const,
};
