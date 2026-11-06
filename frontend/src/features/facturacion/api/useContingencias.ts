import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import type { ReporteBackend } from '@/features/facturacion/api/types';
import type { ReporteFila } from '@/features/facturacion/api/useReportes';

const BASE = '/api/v1/facturacion/contingencias-timbrado';

export type TipoContingencia = 'Pac' | 'Sat' | 'Internet' | 'Certificado' | 'Otra';
export type EstadoContingencia = 'Abierta' | 'Regularizada' | 'Descartada';

export const TIPO_CONTINGENCIA_LABEL: Record<TipoContingencia, string> = {
  Pac: 'PAC',
  Sat: 'SAT',
  Internet: 'Internet',
  Certificado: 'Certificado (CSD)',
  Otra: 'Otra',
};

export const ESTADO_CONTINGENCIA_LABEL: Record<EstadoContingencia, string> = {
  Abierta: 'Abierta',
  Regularizada: 'Regularizada',
  Descartada: 'Descartada (folio quemado)',
};

export interface ContingenciaItem {
  id: string;
  sucursalId: string;
  tipo: TipoContingencia;
  origen: 'Automatica' | 'Manual';
  estado: EstadoContingencia;
  motivo: string;
  errorCodigo: string | null;
  ocurridaAt: string;
  /** Último día para regularizar (yyyy-MM-dd, día 3 del mes siguiente). */
  fechaLimite: string;
  porVencer: boolean;
  vencida: boolean;
  comprobanteId: string | null;
  comprobanteFolio: string | null;
  comprobanteEstado: string | null;
  registradaPor: string | null;
  cerradaAt: string | null;
  uuidFinal: string | null;
  notaCierre: string | null;
  cerradaPor: string | null;
  version: number;
}

export interface ContingenciasResponse {
  items: ContingenciaItem[];
  abiertas: number;
  porVencer: number;
  vencidas: number;
}

export interface ContingenciasFiltros {
  anio: number;
  mes: number;
  estado?: EstadoContingencia;
}

const keys = {
  all: () => [...facturacionKeys.all, 'contingencias-timbrado'] as const,
  lista: (f: ContingenciasFiltros) => [...keys.all(), 'lista', f] as const,
  reporte: (anio: number, mes: number) => [...keys.all(), 'reporte', anio, mes] as const,
};

/** Bitácora de contingencias del mes (F6.8) con los conteos de abiertas por regularizar. */
export function useContingencias(filtros: ContingenciasFiltros) {
  return useQuery<ContingenciasResponse>({
    queryKey: keys.lista(filtros),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams({ anio: String(filtros.anio), mes: String(filtros.mes) });
      if (filtros.estado) params.set('estado', filtros.estado);
      const { data } = await apiRequest<ContingenciasResponse>(`${BASE}?${params.toString()}`, {
        signal,
      });
      return data;
    },
  });
}

/** Reporte mensual (ADR-0036) para <c>&lt;ReporteShell&gt;</c>. */
export function useReporteContingencias(anio: number, mes: number, enabled: boolean) {
  return useQuery<ReporteBackend<ReporteFila>>({
    queryKey: keys.reporte(anio, mes),
    enabled,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ReporteBackend<ReporteFila>>(
        `${BASE}/reporte?anio=${anio}&mes=${mes}`,
        { signal },
      );
      return data;
    },
  });
}

export interface RegistrarContingenciaBody {
  sucursalId: string;
  tipo: TipoContingencia;
  motivo: string;
  ocurridaAt: string;
}

export function useRegistrarContingencia() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { body: RegistrarContingenciaBody; idempotencyKey: string }) => {
      const { data } = await apiRequest<ContingenciaItem>(BASE, {
        method: 'POST',
        body: args.body,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.all() }),
  });
}

export function useRegularizarContingencia() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { id: string; nota: string; idempotencyKey: string }) => {
      const { data } = await apiRequest<ContingenciaItem>(`${BASE}/${args.id}/regularizar`, {
        method: 'POST',
        body: { nota: args.nota },
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.all() }),
  });
}
