import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/** FAC-10: por qué un CFDI no cuadra entre el SAT y el ERP (valores del backend). */
export const TipoDiferenciaSat = {
  FaltanteEnErp: 1,
  FaltanteEnSat: 2,
  EstatusDistinto: 3,
  TotalDistinto: 4,
  TipoDistinto: 5,
  ReceptorDistinto: 6,
} as const;
export type TipoDiferenciaSat = (typeof TipoDiferenciaSat)[keyof typeof TipoDiferenciaSat];

export const TIPO_DIFERENCIA_SAT_LABEL: Record<TipoDiferenciaSat, string> = {
  [TipoDiferenciaSat.FaltanteEnErp]: 'Falta en el ERP',
  [TipoDiferenciaSat.FaltanteEnSat]: 'Falta en el SAT',
  [TipoDiferenciaSat.EstatusDistinto]: 'Estatus distinto',
  [TipoDiferenciaSat.TotalDistinto]: 'Total distinto',
  [TipoDiferenciaSat.TipoDistinto]: 'Tipo distinto',
  [TipoDiferenciaSat.ReceptorDistinto]: 'Receptor distinto',
};

export interface ConteoDiferenciaSat {
  tipo: TipoDiferenciaSat;
  total: number;
  revisadas: number;
}

export interface ConciliacionSat {
  id: string;
  desde: string;
  hasta: string;
  fuente: number;
  nombreArchivo: string;
  ejecutadaEn: string;
  ejecutadaPor: string | null;
  cfdiSat: number;
  cfdiErp: number;
  coincidentes: number;
  conDiferencias: number;
  sinDiferencias: boolean;
  diferenciasPendientes: number;
  porTipo: ConteoDiferenciaSat[];
}

export interface DiferenciaSat {
  id: string;
  uuid: string;
  tipo: TipoDiferenciaSat;
  comprobanteId: string | null;
  folio: string | null;
  rfcReceptor: string | null;
  valorSat: string | null;
  valorErp: string | null;
  revisada: boolean;
  revisadaPor: string | null;
  revisadaEn: string | null;
  nota: string | null;
}

const BASE = '/api/v1/facturacion/conciliacion-sat';
const keys = {
  all: ['facturacion', 'conciliacion-sat'] as const,
  corridas: () => [...keys.all, 'corridas'] as const,
  diferencias: (id: string, tipo: TipoDiferenciaSat | undefined, revisada: boolean | undefined) =>
    [...keys.all, 'diferencias', id, { tipo, revisada }] as const,
};

export function useConciliacionesSat() {
  return useQuery<ConciliacionSat[]>({
    queryKey: keys.corridas(),
    queryFn: async ({ signal }) =>
      (await apiRequest<ConciliacionSat[]>(`${BASE}?limit=20`, { signal })).data,
  });
}

export function useDiferenciasSat(
  id: string | undefined,
  tipo: TipoDiferenciaSat | undefined,
  revisada: boolean | undefined,
) {
  return useQuery<DiferenciaSat[]>({
    queryKey: keys.diferencias(id ?? '', tipo, revisada),
    enabled: !!id,
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (tipo != null) params.set('tipo', String(tipo));
      if (revisada != null) params.set('revisada', String(revisada));
      const qs = params.toString();
      return (
        await apiRequest<DiferenciaSat[]>(`${BASE}/${id}/diferencias${qs ? `?${qs}` : ''}`, {
          signal,
        })
      ).data;
    },
  });
}

/** Sube el archivo de metadatos del SAT (TXT o ZIP) con el periodo que cubre y concilia. */
export function useConciliarCfdiSat() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      archivo: File;
      desde: string;
      hasta: string;
      idempotencyKey: string;
    }) => {
      const formData = new FormData();
      formData.append('archivo', args.archivo);
      formData.append('desde', args.desde);
      formData.append('hasta', args.hasta);
      return (
        await apiRequest<ConciliacionSat>(BASE, {
          method: 'POST',
          body: formData,
          idempotencyKey: args.idempotencyKey,
        })
      ).data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.all }),
  });
}

export function useMarcarDiferenciaSatRevisada() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      conciliacionId: string;
      diferenciaId: string;
      nota: string | null;
      idempotencyKey: string;
    }) =>
      (
        await apiRequest<DiferenciaSat>(
          `${BASE}/${args.conciliacionId}/diferencias/${args.diferenciaId}/revisada`,
          {
            method: 'POST',
            body: { nota: args.nota },
            idempotencyKey: args.idempotencyKey,
          },
        )
      ).data,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.all }),
  });
}
