import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/**
 * TES-09 · Diferencias cambiarias realizadas del periodo.
 * Espejo de `DiferenciasCambiariasQuery.cs` (GET /api/v1/contabilidad/reportes/diferencias-cambiarias): fechas `yyyy-MM-dd`,
 * importes en MXN, enums como texto. Neto = utilidad − pérdida (negativo = pérdida).
 */
export type OrigenDiferenciaCambiaria = 'Proveedores' | 'Clientes';

export interface ColumnaReporteDiferencias {
  clave: string;
  etiqueta: string;
  tipo: 'texto' | 'numero' | 'moneda' | 'fecha';
  alineacion: string | null;
  decimales: number | null;
}

export interface DiferenciaCambiariaFila {
  polizaId: string;
  polizaFolio: string | null;
  fecha: string;
  origen: OrigenDiferenciaCambiaria;
  origenEtiqueta: string;
  documentoFolio: string | null;
  terceroId: string | null;
  terceroClave: string | null;
  terceroNombre: string | null;
  sucursalId: string;
  sucursalNombre: string | null;
  moneda: string | null;
  importeOriginal: number | null;
  tipoCambioDocumento: number | null;
  tipoCambioPago: number | null;
  perdida: number;
  utilidad: number;
  neto: number;
  esAnulacion: boolean;
}

export interface TotalesDiferenciasCambiarias {
  perdida: number;
  utilidad: number;
  neto: number;
  perdidaProveedores: number;
  utilidadProveedores: number;
  perdidaClientes: number;
  utilidadClientes: number;
  movimientos: number;
}

export interface DiferenciasCambiarias {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Record<string, string | null>;
  columnas: ColumnaReporteDiferencias[];
  filas: DiferenciaCambiariaFila[];
  totales: TotalesDiferenciasCambiarias;
  desde: string;
  hasta: string;
  /** Roles cambiarios sin cuenta: sus diferencias no salen en el reporte. */
  avisos: string[];
}

export interface FiltrosDiferenciasCambiarias {
  /** `yyyy-MM-dd`; vacío = primer día del mes de `hasta` (lo resuelve la API). */
  desde?: string;
  /** `yyyy-MM-dd`; vacío = hoy. */
  hasta?: string;
  sucursalId?: string;
  origen?: OrigenDiferenciaCambiaria;
}

const BASE = '/api/v1/contabilidad/reportes/diferencias-cambiarias';

export const diferenciasCambiariasKeys = {
  all: ['contabilidad', 'diferencias-cambiarias'] as const,
  reporte: (f: FiltrosDiferenciasCambiarias) => ['contabilidad', 'diferencias-cambiarias', f] as const,
};

function qs(params: Record<string, unknown>): string {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== null && v !== '') p.set(k, String(v));
  }
  const s = p.toString();
  return s ? `?${s}` : '';
}

export function useDiferenciasCambiarias(filtros: FiltrosDiferenciasCambiarias) {
  return useQuery({
    queryKey: diferenciasCambiariasKeys.reporte(filtros),
    queryFn: async ({ signal }) =>
      (
        await apiRequest<DiferenciasCambiarias>(
          `${BASE}${qs({
            desde: filtros.desde,
            hasta: filtros.hasta,
            sucursalId: filtros.sucursalId,
            origen: filtros.origen,
          })}`,
          { signal },
        )
      ).data,
  });
}
