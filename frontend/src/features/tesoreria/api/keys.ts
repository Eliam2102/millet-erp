/**
 * Query keys de TanStack Query para el módulo Tesorería. Mismo shape que
 * <c>cxcKeys</c> / <c>cxpKeys</c>: <c>['tesoreria', recurso, acción,
 * ...filtros]</c>. TES-FE-PR2 declara cuentas, movimientos y pasivos;
 * las demás familias llegan con su FE-PR.
 */

export interface MovimientosFiltros {
  cuentaBancariaId?: string;
  sentido?: number;
  estadoAplicacion?: number;
  estadoConciliacion?: number;
  desde?: string;
  hasta?: string;
  offset?: number;
  limit?: number;
}

export interface PasivosFiltros {
  proveedorId?: string;
  moneda?: string;
  venceDesde?: string;
  venceHasta?: string;
  montoMinimo?: number;
  montoMaximo?: number;
  soloConSaldo?: boolean;
  offset?: number;
  limit?: number;
}

export interface DepositosFiltros {
  estado?: number;
  clienteId?: string;
  soloPropuestas?: boolean;
  offset?: number;
  limit?: number;
}

export const tesoreriaKeys = {
  all: ['tesoreria'] as const,

  // Cuentas bancarias con saldo (TES-FE-PR2; CRUD TES-7 revisada)
  cuentasAll: () => [...tesoreriaKeys.all, 'cuentas'] as const,
  cuentas: (soloActivas: boolean) =>
    [...tesoreriaKeys.cuentasAll(), { soloActivas }] as const,

  // Libro de movimientos (TES-FE-PR2)
  movimientos: () => [...tesoreriaKeys.all, 'movimientos'] as const,
  movimientosList: (filtros: MovimientosFiltros) =>
    [...tesoreriaKeys.movimientos(), 'list', filtros] as const,
  movimientoById: (id: string) =>
    [...tesoreriaKeys.movimientos(), 'byId', id] as const,

  // Bandeja de pasivos pendientes (TES-FE-PR2)
  pasivos: () => [...tesoreriaKeys.all, 'pasivos'] as const,
  pasivosList: (filtros: PasivosFiltros) =>
    [...tesoreriaKeys.pasivos(), 'list', filtros] as const,

  // Pagos a cuenta abiertos (TES-FE-PR4)
  pagosCuenta: () => [...tesoreriaKeys.all, 'pagos-cuenta'] as const,
  pagosCuentaList: (filtros: Record<string, unknown>) =>
    [...tesoreriaKeys.pagosCuenta(), 'list', filtros] as const,

  // Depósitos por confirmar (TES-FE-PR4b)
  depositos: () => [...tesoreriaKeys.all, 'depositos'] as const,
  depositosList: (filtros: DepositosFiltros) =>
    [...tesoreriaKeys.depositos(), 'list', filtros] as const,

  // REPP recibido de proveedor (TES-FE-PR6b)
  repp: () => [...tesoreriaKeys.all, 'repp'] as const,
  reppPendientesList: (filtros: Record<string, unknown>) =>
    [...tesoreriaKeys.repp(), 'pendientes', filtros] as const,

  // Reportes ADR-0036 (TES-FE-PR6)
  reportes: () => [...tesoreriaKeys.all, 'reportes'] as const,
  flujoEfectivo: (filtros: Record<string, unknown>) =>
    [...tesoreriaKeys.reportes(), 'flujo-efectivo', filtros] as const,
  auxiliarBancos: (filtros: Record<string, unknown>) =>
    [...tesoreriaKeys.reportes(), 'auxiliar-bancos', filtros] as const,
};
