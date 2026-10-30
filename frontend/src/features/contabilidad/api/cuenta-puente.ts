import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/**
 * K10.5 · Conciliación de la cuenta puente recepción–factura por OC.
 * Espejo de `CuentaPuenteQueries.cs` (GET /api/v1/contabilidad/reportes/cuenta-puente): fechas `yyyy-MM-dd` (DateOnly),
 * importes en MXN sin IVA y enums como texto. Saldo de la puente = cargos (facturado) − abonos (recibido).
 */
export type NaturalezaSaldoPuente = 'Saldada' | 'FacturadoSinRecibir' | 'RecibidoSinFacturar';
export type TramoAntiguedadPuente = 'De0a30' | 'De31a60' | 'MasDe60';
export type TipoDocumentoPuente =
  | 'Recepcion'
  | 'Factura'
  | 'Devolucion'
  | 'NotaCargo'
  | 'NotaCredito'
  | 'CapitalizacionAf'
  | 'Anulacion';

export interface ColumnaReportePuente {
  /** Propiedad camelCase de la fila. */
  clave: string;
  etiqueta: string;
  tipo: 'texto' | 'numero' | 'moneda' | 'fecha';
  alineacion: string | null;
}

export interface CuentaPuenteRef {
  id: string;
  codigo: string;
  nombre: string;
}

/** Una fila del reporte: saldo de la puente de una OC al corte. */
export interface SaldoPuenteOc {
  ordenCompraId: string;
  ordenCompraFolio: string | null;
  proveedorId: string | null;
  proveedorClave: string | null;
  proveedorNombre: string | null;
  sucursalIds: string[];
  sucursalNombre: string | null;
  cargos: number;
  abonos: number;
  saldo: number;
  naturaleza: NaturalezaSaldoPuente;
  naturalezaEtiqueta: string;
  fechaPrimerMovimientoSinSaldar: string | null;
  diasAntiguedad: number | null;
  tramo: TramoAntiguedadPuente | null;
  tramoEtiqueta: string | null;
  /** Informativa (OC en moneda extranjera): residuo atribuible al tipo de cambio; no se asienta. */
  diferenciaCambiaria: number | null;
  ultimoMovimiento: string;
}

/**
 * Con filtro de proveedor no se concilia contra la balanza (la balanza no se abre por proveedor):
 * `saldoBalanza`, `partidasSinOc` y `cuadra` llegan en null.
 */
export interface TotalesConciliacionPuente {
  totalReporte: number;
  saldoBalanza: number | null;
  partidasSinOc: number | null;
  cuadra: boolean | null;
  ordenesConSaldo: number;
  facturadoSinRecibir: number;
  recibidoSinFacturar: number;
  tramo0a30: number;
  tramo31a60: number;
  tramoMasDe60: number;
}

export interface ConciliacionPuente {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Record<string, string | null>;
  columnas: ColumnaReportePuente[];
  filas: SaldoPuenteOc[];
  totales: TotalesConciliacionPuente;
  corte: string;
  cuentaPuente: CuentaPuenteRef;
}

export interface MovimientoPuente {
  id: string;
  polizaId: string;
  polizaFolio: string | null;
  fecha: string;
  tipoDocumento: TipoDocumentoPuente;
  documentoId: string;
  facturaOrigenId: string | null;
  lineaOcId: string | null;
  sucursalId: string;
  cargo: number;
  abono: number;
  saldoAcumulado: number;
  moneda: string;
  tipoCambio: number;
  importeOriginal: number | null;
  esReversa: boolean;
  movimientoRevertidoId: string | null;
}

export interface MovimientosPuenteOc {
  ordenCompraId: string;
  ordenCompraFolio: string | null;
  proveedorId: string | null;
  proveedorNombre: string | null;
  corte: string;
  cuentaPuente: CuentaPuenteRef;
  saldo: SaldoPuenteOc;
  movimientos: MovimientoPuente[];
}

export interface FiltrosConciliacionPuente {
  /** `yyyy-MM-dd`; vacío = hoy (lo resuelve la API). */
  corte?: string;
  sucursalId?: string;
  proveedorId?: string;
  soloConSaldo: boolean;
}

const BASE = '/api/v1/contabilidad/reportes/cuenta-puente';

export const cuentaPuenteKeys = {
  all: ['contabilidad', 'cuenta-puente'] as const,
  conciliacion: (f: FiltrosConciliacionPuente) => ['contabilidad', 'cuenta-puente', 'conciliacion', f] as const,
  movimientos: (ocId: string, corte: string) => ['contabilidad', 'cuenta-puente', 'movimientos', ocId, corte] as const,
};

function qs(params: Record<string, unknown>): string {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== null && v !== '') p.set(k, String(v));
  }
  const s = p.toString();
  return s ? `?${s}` : '';
}

/** Conciliación por OC al corte. `soloConSaldo` viaja siempre (la API lo toma como true si se omite). */
export function useConciliacionPuente(filtros: FiltrosConciliacionPuente, enabled = true) {
  return useQuery({
    queryKey: cuentaPuenteKeys.conciliacion(filtros),
    enabled,
    queryFn: async ({ signal }) =>
      (
        await apiRequest<ConciliacionPuente>(
          `${BASE}${qs({
            corte: filtros.corte,
            sucursalId: filtros.sucursalId,
            proveedorId: filtros.proveedorId,
            soloConSaldo: filtros.soloConSaldo,
          })}`,
          { signal },
        )
      ).data,
  });
}

/** Movimientos de la puente de una OC al corte con su saldo acumulado. Sin OC no se consulta. */
export function useMovimientosPuenteOc(ocId: string | null, corte: string) {
  return useQuery({
    queryKey: cuentaPuenteKeys.movimientos(ocId ?? '', corte),
    enabled: !!ocId,
    queryFn: async ({ signal }) =>
      (await apiRequest<MovimientosPuenteOc>(`${BASE}/${ocId}/movimientos${qs({ corte })}`, { signal })).data,
  });
}
