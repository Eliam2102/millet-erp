import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import type { EjecucionSyncAceptada } from '@/modules/datos-maestros/api/types';

export interface CampoDuenoAw { entidad: string; campo: string; dueno: 'AW' | 'ERP'; siDifiere: string }
export interface MatrizAw { version: string; campos: CampoDuenoAw[] }
export type TipoCambioAw = 'Aplicado' | 'EditadoEnErp' | 'RfcCompartido' | 'Baja' | 'Reactivacion' | 'Diferencia';
export type EstadoCambioAw = 'Informativo' | 'Pendiente' | 'Resuelto';
export interface CambioMaestroAw {
  id: string;
  tipoEntidad: string;
  entidadId: string | null;
  referenciaExterna: string;
  campo: string;
  valorAntes: string | null;
  valorDespues: string | null;
  dueno: 'AW' | 'ERP';
  tipo: TipoCambioAw;
  estado: EstadoCambioAw;
  causa: string;
  versionEvento: string;
  versionMatriz: string;
  ejecucionId: string | null;
  ocurridoEnUtc: string;
  resolucion: string | null;
  resueltoEnUtc: string | null;
  version: number;
}

export const SINCRONIZACION_AW_KEY = ['datos-maestros', 'sincronizacion-aw'] as const;
const base = '/api/v1/datos-maestros';

export function useMatrizAw() {
  return useQuery({
    queryKey: [...SINCRONIZACION_AW_KEY, 'matriz'],
    queryFn: async ({ signal }) => (await apiRequest<MatrizAw>(`${base}/sincronizacion-aw/matriz`, { signal })).data,
  });
}

export function useCambiosAwCliente(id: string | null, enabled: boolean) {
  return useQuery({
    queryKey: [...SINCRONIZACION_AW_KEY, 'cambios', 'Cliente', id],
    queryFn: async ({ signal }) =>
      (await apiRequest<CambioMaestroAw[]>(`${base}/clientes/${id}/cambios-aw`, { signal })).data,
    enabled: enabled && id != null,
  });
}

export function useCambiosAwProducto(id: string | null, enabled: boolean) {
  return useQuery({
    queryKey: [...SINCRONIZACION_AW_KEY, 'cambios', 'ProductoAw', id],
    queryFn: async ({ signal }) =>
      (await apiRequest<CambioMaestroAw[]>(`${base}/productos-aw/${id}/cambios-aw`, { signal })).data,
    enabled: enabled && id != null,
  });
}

export type EntidadConflictoAw = 'clientes' | 'productos-aw';
export type FiltroEstadoAw = 'Pendiente' | 'Resuelto' | 'Todos';

export function useConflictosAw(entidad: EntidadConflictoAw, estado: FiltroEstadoAw, enabled: boolean, ejecucionId?: string) {
  return useQuery({
    queryKey: [...SINCRONIZACION_AW_KEY, 'conflictos', entidad, estado, ejecucionId ?? null],
    queryFn: async ({ signal }) =>
      (await apiRequest<CambioMaestroAw[]>(
        `${base}/${entidad}/conflictos-aw?estado=${estado}${ejecucionId ? `&ejecucionId=${ejecucionId}` : ''}`, { signal })).data,
    enabled,
  });
}

export function useResolverConflictoAw() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ entidad, id, resolucion, version }: {
      entidad: EntidadConflictoAw; id: string; resolucion: string; version: number;
    }) => (await apiRequest<void>(`${base}/${entidad}/conflictos-aw/${id}/resolver`, {
      method: 'POST', body: { resolucion, version }, idempotencyKey: crypto.randomUUID(),
    })).data,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: SINCRONIZACION_AW_KEY }),
  });
}

type Variante = 'info' | 'warning' | 'success' | 'danger' | 'neutral';
export const TIPO_CAMBIO_AW: Record<TipoCambioAw, { texto: string; variante: Variante }> = {
  Aplicado: { texto: 'Aplicado', variante: 'info' },
  Baja: { texto: 'Baja', variante: 'warning' },
  Reactivacion: { texto: 'Reactivación', variante: 'success' },
  EditadoEnErp: { texto: 'Editado en ERP', variante: 'warning' },
  RfcCompartido: { texto: 'RFC repetido', variante: 'danger' },
  Diferencia: { texto: 'Diferencia (ERP conserva)', variante: 'neutral' },
};

// ─── Corrida de clientes + productos y reporte de diferencias (ADM-06 R4) ──

/** Reporte de diferencias en el JSON de ADR-0036; compatible con `ReporteParaExportar`. */
export interface ReporteDiferenciasAw {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Record<string, string | null>;
  columnas: { clave: string; etiqueta: string; tipo: 'texto' | 'numero' | 'moneda' | 'fecha' }[];
  filas: Record<string, string | null>[];
  totales: Record<string, number>;
  truncado: boolean;
}

/** Resumen del barrido de productos guardado en la ejecución (o `{ error, mensaje }` si el origen no respondió). */
export interface ResumenProductosAw {
  leidos?: number; creados?: number; actualizados?: number; sinCambios?: number; errores?: number;
  error?: string; mensaje?: string;
}

export function fechaCorridaAw(v: string | null | undefined) {
  return v ? new Date(v).toLocaleString('es-MX') : '—';
}

export function leerResumenProductos(json: string | null | undefined): ResumenProductosAw | null {
  if (!json) return null;
  try { return JSON.parse(json) as ResumenProductosAw; } catch { return null; }
}

export function useIniciarCorridaAw() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => (await apiRequest<EjecucionSyncAceptada>(`${base}/sincronizacion-aw/corridas`, {
      method: 'POST', idempotencyKey: crypto.randomUUID(),
    })).data,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['datos-maestros'] });
    },
  });
}

export async function obtenerReporteDiferenciasAw(id: string) {
  return (await apiRequest<ReporteDiferenciasAw>(`${base}/sincronizacion-aw/corridas/${id}/diferencias`)).data;
}
