import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import type { PagedResponse } from './types';
import { tesoreriaKeys } from './keys';

/** Estados de la diferencia de corte (espejo de EstadoDiferenciaCorte). */
export const EstadoDiferenciaCorte = {
  PendienteAutorizar: 1,
  Autorizada: 2,
  PorAclarar: 3,
  Saldada: 4,
  SaldadaParcial: 5,
} as const;
export type EstadoDiferenciaCorte =
  (typeof EstadoDiferenciaCorte)[keyof typeof EstadoDiferenciaCorte];

export const ESTADO_DIFERENCIA_CORTE_LABELS: Record<EstadoDiferenciaCorte, string> = {
  [EstadoDiferenciaCorte.PendienteAutorizar]: 'Pendiente de autorizar',
  [EstadoDiferenciaCorte.Autorizada]: 'Autorizada',
  [EstadoDiferenciaCorte.PorAclarar]: 'Por aclarar',
  [EstadoDiferenciaCorte.Saldada]: 'Saldada',
  [EstadoDiferenciaCorte.SaldadaParcial]: 'Saldada parcialmente',
};

export interface DiferenciaCorteResponse {
  id: string;
  cajaSesionId: string;
  cajaId: string;
  sucursalId: string;
  cajeroId: string;
  cajeroNombre?: string | null;
  cajaNombre?: string | null;
  diaCorte: string;
  /** Con signo: negativo faltante, positivo sobrante. */
  importe: number;
  tipo: 'Faltante' | 'Sobrante';
  estado: EstadoDiferenciaCorte;
  motivoRechazo: string | null;
  importeDevuelto: number;
  pendientePorDevolver: number;
  resueltaPor: string | null;
  resueltaEn: string | null;
  recibidoEn: string;
  version: number;
}

const BASE = '/api/v1/tesoreria/diferencias-corte';
const KEY = [...tesoreriaKeys.all, 'diferencias-corte'] as const;

export function useDiferenciasCorte(filtros: { estado?: number; offset?: number; limit?: number } = {}) {
  return useQuery<PagedResponse<DiferenciaCorteResponse>>({
    queryKey: [...KEY, filtros],
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (filtros.estado != null) params.set('estado', String(filtros.estado));
      if (filtros.offset != null) params.set('offset', String(filtros.offset));
      if (filtros.limit != null) params.set('limit', String(filtros.limit));
      const qs = params.toString();
      const { data } = await apiRequest<PagedResponse<DiferenciaCorteResponse>>(
        qs ? `${BASE}?${qs}` : BASE,
        { signal },
      );
      return data;
    },
  });
}

type Args = { id: string; versionEsperada: number; idempotencyKey: string };

function useResolverDiferencia<T extends Args>(accion: (a: T) => { path: string; body?: unknown }) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: T) => {
      const { path, body } = accion(args);
      const { data } = await apiRequest<DiferenciaCorteResponse>(`${BASE}/${args.id}${path}`, {
        method: 'POST',
        body,
        idempotencyKey: args.idempotencyKey,
        headers: { 'X-Expected-Version': String(args.versionEsperada) },
      });
      return data;
    },
    onSuccess: () => {
      // Autorizar cambia también si el corte queda conciliado (bandeja de depósitos).
      queryClient.invalidateQueries({ queryKey: tesoreriaKeys.all });
    },
  });
}

/** El cajero del corte no autoriza el suyo: el servidor responde 422 `DIF_CAJERO_MISMO_USUARIO`. */
export function useAutorizarDiferenciaCorte() {
  return useResolverDiferencia<Args>(() => ({ path: '/autorizar' }));
}

/** El rechazo exige motivo y deja la diferencia «Por aclarar». */
export function useRechazarDiferenciaCorte() {
  return useResolverDiferencia((a: Args & { motivo: string }) => ({
    path: '/rechazar',
    body: { motivo: a.motivo },
  }));
}
