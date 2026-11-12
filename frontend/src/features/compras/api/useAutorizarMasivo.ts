import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { comprasKeys } from '@/features/compras/api/keys';
import type { NivelAutorizacion } from '@/features/compras/api/types';

/** Resultado por id de un lote (autorizar, rechazar o devolver). */
export interface AutorizacionMasivaItemResultado {
  id: string;
  ok: boolean;
  errorCodigo: string | null;
  mensaje: string | null;
}

export interface AutorizacionMasivaResponse {
  resultados: AutorizacionMasivaItemResultado[];
}

/**
 * Requisición de un lote con la versión que vio la selección (M-254): si cambió
 * antes de ejecutar, el backend responde <c>RQ_CAMBIO_DESDE_SELECCION</c> en esa fila.
 */
export interface LoteItem {
  id: string;
  version: number;
}

export interface AutorizarMasivoArgs {
  /** Entre 1 y 50 requisiciones, todas del mismo nivel pendiente. */
  items: readonly LoteItem[];
  nivel: NivelAutorizacion;
  notas?: string | null;
  idempotencyKey: string;
}

export interface RechazarMasivoArgs {
  items: readonly LoteItem[];
  motivoId: string;
  motivoTexto: string | null;
  idempotencyKey: string;
}

export interface DevolverMasivoArgs {
  items: readonly LoteItem[];
  comentario: string;
  idempotencyKey: string;
}

const BASE = '/api/v1/compras/requisiciones';

/**
 * Mutación de lote: el backend responde 200 con un resultado por id; un id
 * fallido no anula el lote. La UI debe leer <c>resultados</c>, no solo el
 * éxito HTTP. Idempotency-Key obligatorio (ADR-0020). Tras la respuesta se
 * invalida la familia de compras para refrescar la bandeja.
 */
function useLote<TArgs extends { idempotencyKey: string }>(
  ruta: string,
  cuerpo: (args: TArgs) => Record<string, unknown>,
) {
  const queryClient = useQueryClient();
  return useMutation<AutorizacionMasivaResponse, Error, TArgs>({
    mutationFn: async (args) => {
      const { data } = await apiRequest<AutorizacionMasivaResponse>(`${BASE}/${ruta}`, {
        method: 'POST',
        body: cuerpo(args),
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: comprasKeys.all }),
  });
}

/** <c>POST /requisiciones/autorizaciones-masivas</c> (S2, versión fija M-254). */
export function useAutorizarMasivo() {
  return useLote<AutorizarMasivoArgs>('autorizaciones-masivas', ({ items, nivel, notas }) => ({
    items,
    nivel,
    notas: notas ?? null,
  }));
}

/** <c>POST /requisiciones/rechazos-masivos</c> (M-254): un motivo para todo el lote. */
export function useRechazarMasivo() {
  return useLote<RechazarMasivoArgs>('rechazos-masivos', ({ items, motivoId, motivoTexto }) => ({
    items,
    motivoId,
    motivoTexto,
  }));
}

/** <c>POST /requisiciones/devoluciones-masivas</c> (M-254): un comentario para todo el lote. */
export function useDevolverMasivo() {
  return useLote<DevolverMasivoArgs>('devoluciones-masivas', ({ items, comentario }) => ({
    items,
    comentario,
  }));
}
