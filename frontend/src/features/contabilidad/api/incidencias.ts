import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { periodoKeys } from './periodos';

/** Incidencias posteriores (M-321): lo hallado después de autorizar una póliza o cerrar un periodo. */
const BASE = '/api/v1/contabilidad/incidencias-posteriores';

export type EstadoIncidencia = 'Abierta' | 'Resuelta';
export type ResolucionIncidencia = 'PolizaAjuste' | 'ReaperturaAcotada';

export interface Incidencia {
  id: string;
  polizaId: string | null;
  polizaFolio: string | null;
  periodoAnio: number;
  periodoNumero: number;
  periodo: string;
  hallazgo: string;
  impacto: string;
  importeImpacto: number | null;
  estado: EstadoIncidencia;
  registradaPor: string;
  registradaEn: string;
  resolucion: ResolucionIncidencia | null;
  polizaAjusteId: string | null;
  polizaAjusteFolio: string | null;
  reaperturaId: string | null;
  notaResolucion: string | null;
  resueltaPor: string | null;
  resueltaEn: string | null;
}

export interface RegistrarIncidenciaBody {
  polizaId: string | null;
  anio: number | null;
  numero: number | null;
  hallazgo: string;
  impacto: string;
  importeImpacto: number | null;
}

export interface ResolverIncidenciaBody {
  polizaAjusteId: string | null;
  reaperturaId: string | null;
  nota: string | null;
}

export const incidenciaKeys = {
  all: ['contabilidad', 'incidencias'] as const,
  lista: (estado: EstadoIncidencia | undefined) => ['contabilidad', 'incidencias', 'lista', estado ?? 'todas'] as const,
};

export function useIncidencias(estado?: EstadoIncidencia) {
  return useQuery({
    queryKey: incidenciaKeys.lista(estado),
    queryFn: async ({ signal }) =>
      (await apiRequest<Incidencia[]>(`${BASE}${estado ? `?estado=${estado}` : ''}`, { signal })).data,
  });
}

/** Registrar o resolver mueve el paso «incidencias» del checklist de cierre: se invalida también lo de periodos. */
function useInvalidar() {
  const qc = useQueryClient();
  return () => {
    void qc.invalidateQueries({ queryKey: incidenciaKeys.all });
    void qc.invalidateQueries({ queryKey: periodoKeys.all });
  };
}

export function useRegistrarIncidencia() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { body: RegistrarIncidenciaBody; idempotencyKey: string }) =>
      (await apiRequest<Incidencia>(BASE, { method: 'POST', body: a.body, idempotencyKey: a.idempotencyKey })).data,
    onSuccess: invalidar,
  });
}

export function useResolverIncidencia() {
  const invalidar = useInvalidar();
  return useMutation({
    mutationFn: async (a: { id: string; body: ResolverIncidenciaBody; idempotencyKey: string }) =>
      (await apiRequest<Incidencia>(`${BASE}/${a.id}/resolver`, { method: 'POST', body: a.body, idempotencyKey: a.idempotencyKey })).data,
    onSuccess: invalidar,
  });
}
