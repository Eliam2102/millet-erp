import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/**
 * F1-CON-08 · K10.10 · Balance general y estado de resultados. Espejo de `EstadosFinancierosQuery.cs`
 * (GET /api/v1/contabilidad/reportes/{balance-general|estado-resultados}). Importes con el signo de su sección: activo,
 * costos y gastos en positivo cuando son deudores; pasivo, capital e ingresos en positivo cuando son acreedores.
 */
export type EstadoFinanciero = 'BalanceGeneral' | 'EstadoResultados';

export type TipoFilaEstado = 'seccion' | 'rubro' | 'cuenta' | 'calculo' | 'subtotal' | 'total' | 'resultado';

export interface ColumnaEstadoFinanciero {
  clave: string;
  etiqueta: string;
  tipo: 'texto' | 'numero' | 'moneda' | 'fecha';
  alineacion: string | null;
  decimales: number | null;
}

export interface FilaEstadoFinanciero {
  clave: string;
  tipo: TipoFilaEstado;
  concepto: string;
  cuenta: string | null;
  sangria: number;
  actual: number | null;
  comparativo: number | null;
  variacion: number | null;
  porcentaje: number | null;
  cuentaId: string | null;
}

export interface TotalesEstadoFinanciero {
  activo: number;
  pasivo: number;
  capital: number;
  pasivoMasCapital: number;
  diferencia: number;
  ingresos: number;
  costo: number;
  utilidadBruta: number;
  gastos: number;
  utilidadOperacion: number;
  financiamiento: number;
  otrosResultados: number;
  utilidadPeriodo: number;
  sinClasificar: number;
  cuadra: boolean;
}

export interface CuentaSinClasificar {
  cuentaId: string;
  codigo: string;
  nombre: string;
  saldo: number;
  motivo: string;
}

export interface EstadoFinancieroReporte {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Record<string, string | null>;
  columnas: ColumnaEstadoFinanciero[];
  filas: FilaEstadoFinanciero[];
  totales: TotalesEstadoFinanciero;
  totalesComparativo: TotalesEstadoFinanciero | null;
  sinClasificar: CuentaSinClasificar[];
  avisos: string[];
  estado: EstadoFinanciero;
  anio: number;
  periodo: number;
  desde: number;
  comparar: boolean;
  sucursalId: string | null;
}

export interface FiltrosEstadoFinanciero {
  anio: number;
  periodo: number;
  /** Solo estado de resultados: periodos 1 al indicado (true) o solo el periodo (false). */
  acumulado?: boolean;
  comparar?: boolean;
  sucursalId?: string;
}

const RUTA: Record<EstadoFinanciero, string> = {
  BalanceGeneral: 'balance-general',
  EstadoResultados: 'estado-resultados',
};

function qs(f: FiltrosEstadoFinanciero, estado: EstadoFinanciero): string {
  const p = new URLSearchParams({ anio: String(f.anio), periodo: String(f.periodo) });
  if (estado === 'EstadoResultados' && f.acumulado !== undefined) p.set('acumulado', String(f.acumulado));
  if (f.comparar) p.set('comparar', 'true');
  if (f.sucursalId) p.set('sucursalId', f.sucursalId);
  return `?${p.toString()}`;
}

/** Balance general o estado de resultados. Sin filtros aplicados no se consulta. */
export function useEstadoFinanciero(estado: EstadoFinanciero, filtros: FiltrosEstadoFinanciero | null) {
  return useQuery({
    queryKey: ['contabilidad', 'reportes', RUTA[estado], filtros] as const,
    enabled: !!filtros,
    queryFn: async ({ signal }) =>
      (
        await apiRequest<EstadoFinancieroReporte>(
          `/api/v1/contabilidad/reportes/${RUTA[estado]}${qs(filtros!, estado)}`,
          { signal },
        )
      ).data,
  });
}

// ─── Integraciones del auditor (R24) ───

export type IntegracionContable = 'Ingresos' | 'Costo' | 'Gastos' | 'Financiamiento' | 'Compras';

export interface FilaIntegracion {
  cuentaId: string;
  cuentaCodigo: string;
  cuentaNombre: string;
  mayorCodigo: string;
  terceroTipo: 'Cliente' | 'Proveedor' | null;
  terceroId: string | null;
  terceroClave: string | null;
  terceroNombre: string;
  cargos: number;
  abonos: number;
  importe: number;
  polizas: number;
}

export interface TotalesIntegracion {
  cargos: number;
  abonos: number;
  importe: number;
  sinTercero: number;
  cuentas: number;
  terceros: number;
}

export interface IntegracionReporte {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Record<string, string | null>;
  columnas: ColumnaEstadoFinanciero[];
  filas: FilaIntegracion[];
  totales: TotalesIntegracion;
  avisos: string[];
  tipo: IntegracionContable;
  anio: number;
  desde: number;
  hasta: number;
  sucursalId: string | null;
}

export interface FiltrosIntegracion {
  tipo: IntegracionContable;
  anio: number;
  desde: number;
  hasta: number;
  sucursalId?: string;
}

/** Integración por cuenta y cliente o proveedor. Sin filtros aplicados no se consulta. */
export function useIntegracion(filtros: FiltrosIntegracion | null) {
  return useQuery({
    queryKey: ['contabilidad', 'reportes', 'integracion', filtros] as const,
    enabled: !!filtros,
    queryFn: async ({ signal }) => {
      const f = filtros!;
      const p = new URLSearchParams({ tipo: f.tipo, anio: String(f.anio), desde: String(f.desde), hasta: String(f.hasta) });
      if (f.sucursalId) p.set('sucursalId', f.sucursalId);
      return (await apiRequest<IntegracionReporte>(`/api/v1/contabilidad/reportes/integracion?${p.toString()}`, { signal }))
        .data;
    },
  });
}
