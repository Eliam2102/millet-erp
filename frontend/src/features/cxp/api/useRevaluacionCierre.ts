import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { cxpKeys } from '@/features/cxp/api/keys';
import type { BackendReporteResponse } from '@/features/cxp/api/useReportes';

/** Revaluación de cierre de saldos por pagar en moneda extranjera (CXP-12). */

const BASE = '/api/v1/cuentas-por-pagar/revaluaciones';

export interface RevaluacionCierreListItem {
  id: string;
  anio: number;
  mes: number;
  fechaCierre: string;
  fechaReversa: string;
  totalPerdida: number;
  totalUtilidad: number;
  documentos: number;
  revertida: boolean;
}

export interface RevaluacionOmitida {
  facturaId: string;
  documento: string;
  motivo: string;
}

export interface RevaluacionCierreResumen {
  id: string;
  anio: number;
  mes: number;
  fechaCierre: string;
  fechaReversa: string;
  documentos: number;
  totalPerdida: number;
  totalUtilidad: number;
  omitidas: RevaluacionOmitida[];
}

const keyBase = [...cxpKeys.all, 'revaluaciones'] as const;

export function useRevaluacionesCierre() {
  return useQuery({
    queryKey: keyBase,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<RevaluacionCierreListItem[]>(BASE, { signal });
      return data;
    },
  });
}

export function useReporteRevaluacionCierre(periodo: { anio: number; mes: number } | null) {
  return useQuery({
    queryKey: [...keyBase, 'reporte', periodo] as const,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<BackendReporteResponse>(
        `${BASE}/${periodo!.anio}/${periodo!.mes}/reporte`,
        { signal },
      );
      return data;
    },
    enabled: periodo != null,
  });
}

export function useEjecutarRevaluacionCierre() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { anio: number; mes: number; idempotencyKey: string }) => {
      const { data } = await apiRequest<RevaluacionCierreResumen>(BASE, {
        method: 'POST',
        body: { anio: args.anio, mes: args.mes },
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keyBase });
    },
  });
}
