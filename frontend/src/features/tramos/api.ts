import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, useBodyScopedIdempotencyKey } from '@/lib/api';

/** M-303: tramo de la operación que se puede detener (p. ej. `pt.captura`). */
export interface TramoDto {
  clave: string;
  nombre: string;
  modulo: string;
  detenido: boolean;
  motivo: string | null;
  detenidoPorId: string | null;
  detenidoAt: string | null;
  incidenciaId: string | null;
  ultimoEventoCorrectoAt: string | null;
  ultimoEventoCorrecto: string | null;
  reanudadoPorId: string | null;
  reanudadoAt: string | null;
  motivoReanudacion: string | null;
}

const BASE = '/api/v1/tramos';
export const tramosKey = ['tramos'] as const;

export function useTramos(opciones?: { refetchInterval?: number }) {
  return useQuery({
    queryKey: tramosKey,
    queryFn: async ({ signal }) => (await apiRequest<TramoDto[]>(BASE, { signal })).data,
    ...opciones,
  });
}

/** Detener o reanudar. Reanudar debe hacerlo otra persona: el servidor lo exige y devuelve el motivo. */
export function useAccionTramo() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  return useMutation({
    mutationFn: async ({ clave, accion, motivo }: { clave: string; accion: 'detener' | 'reanudar'; motivo: string }) =>
      (await apiRequest<TramoDto>(`${BASE}/${encodeURIComponent(clave)}/${accion}`, { method: 'POST', body: { motivo }, idempotencyKey: keyFor({ clave, accion, motivo }) })).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: tramosKey }),
  });
}
