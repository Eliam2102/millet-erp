import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { comprasKeys } from '@/features/compras/api/keys';
import type { LineaValues } from '@/features/compras/schemas/linea';
import type { ActualizarNotasLineaValues } from '@/features/compras/schemas/notas-linea';
import type { LineaResponse } from '@/features/compras/api/types';

/**
 * Hooks de mutación de líneas (doc 05 §6, §7.4-7.6).
 *
 * <para>Convención de invalidación: tras éxito, todos invalidan
 * <c>comprasKeys.requisicion(rqId)</c> para que P3 refleje el cambio.
 * Como bonus, invalidan también la lista (<c>requisiciones</c>) por si
 * el usuario tiene la bandeja en otra tab.</para>
 *
 * <para>Convención de Idempotency-Key:</para>
 * <list>
 *   <item><c>useAgregarLinea</c> → <b>obligatorio</b> (POST nuevo;
 *   ADR-0020).</item>
 *   <item><c>useActualizarLinea</c> → no requiere (PATCH idempotente
 *   por shape).</item>
 *   <item><c>useActualizarNotasLinea</c> → no requiere; **optimistic
 *   update** porque el comprador escribe rápido y el roundtrip se
 *   siente lento (doc 05 §7.4 excepción).</item>
 *   <item><c>useEliminarLinea</c> → no requiere (DELETE idempotente).
 *   </item>
 * </list>
 */

// ─── Agregar línea ──────────────────────────────────────────────────

/** Mirror de <c>AgregarLineaResponse</c> backend. */
export interface AgregarLineaResponse {
  id: string;
  requisicionId: string;
  posicion: number;
  version: number;
}

export interface AgregarLineaArgs {
  requisicionId: string;
  values: LineaValues;
  /** UUID v4 estable (vía <c>useFormIdempotencyKey</c>). ADR-0020. */
  idempotencyKey: string;
}

export function useAgregarLinea() {
  const queryClient = useQueryClient();
  return useMutation<AgregarLineaResponse, Error, AgregarLineaArgs>({
    mutationFn: async ({ requisicionId, values, idempotencyKey }) => {
      const { data } = await apiRequest<AgregarLineaResponse>(
        `/api/v1/compras/requisiciones/${requisicionId}/lineas`,
        { method: 'POST', body: values, idempotencyKey },
      );
      return data;
    },
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({
        queryKey: comprasKeys.requisicion(vars.requisicionId),
      });
      queryClient.invalidateQueries({
        queryKey: comprasKeys.requisiciones(),
      });
    },
  });
}

// ─── Actualizar línea (estructural) ─────────────────────────────────

export interface ActualizarLineaArgs {
  requisicionId: string;
  lineaId: string;
  values: Omit<LineaValues, 'notas'>; // notas tienen endpoint propio
}

export function useActualizarLinea() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, ActualizarLineaArgs>({
    mutationFn: async ({ requisicionId, lineaId, values }) => {
      await apiRequest<void>(
        `/api/v1/compras/requisiciones/${requisicionId}/lineas/${lineaId}`,
        { method: 'PATCH', body: values },
      );
    },
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({
        queryKey: comprasKeys.requisicion(vars.requisicionId),
      });
    },
  });
}

// ─── Actualizar notas (optimistic) ──────────────────────────────────

export interface ActualizarNotasLineaArgs {
  requisicionId: string;
  lineaId: string;
  values: ActualizarNotasLineaValues;
}

/**
 * <b>Optimistic update</b> (doc 05 §7.4 excepción): el comprador
 * escribe rápido en la textarea inline y el roundtrip se siente
 * lento; aplicamos el valor al cache inmediatamente y revertimos si
 * falla. Toast de error en <c>onError</c> a cargo del caller.
 */
export function useActualizarNotasLinea() {
  const queryClient = useQueryClient();

  return useMutation<
    void,
    Error,
    ActualizarNotasLineaArgs,
    { previa: unknown }
  >({
    mutationFn: async ({ requisicionId, lineaId, values }) => {
      await apiRequest<void>(
        `/api/v1/compras/requisiciones/${requisicionId}/lineas/${lineaId}/notas`,
        { method: 'PATCH', body: values },
      );
    },
    onMutate: async ({ requisicionId, lineaId, values }) => {
      const queryKey = comprasKeys.requisicion(requisicionId);
      // Cancelar refetches en flight para que no pisen nuestra
      // actualización optimistic.
      await queryClient.cancelQueries({ queryKey });

      // Capturar la snapshot anterior para revertir si falla.
      const previa = queryClient.getQueryData(queryKey);

      // Mutar el cache: la query de useRequisicion guarda
      // { data, etag } — modificamos data.lineas[i].notas.
      queryClient.setQueryData(
        queryKey,
        (cached: unknown) => {
          if (cached == null || typeof cached !== 'object') return cached;
          const c = cached as { data?: { lineas?: LineaResponse[] }; etag?: string };
          if (c.data == null || !Array.isArray(c.data.lineas)) return cached;
          return {
            ...c,
            data: {
              ...c.data,
              lineas: c.data.lineas.map((l) =>
                l.id === lineaId ? { ...l, notas: values.notas } : l,
              ),
            },
          };
        },
      );

      return { previa };
    },
    onError: (_err, vars, context) => {
      if (context?.previa !== undefined) {
        queryClient.setQueryData(
          comprasKeys.requisicion(vars.requisicionId),
          context.previa,
        );
      }
    },
    onSettled: (_data, _err, vars) => {
      // Sea éxito o error revertido, refetch para sincronizar con BD.
      queryClient.invalidateQueries({
        queryKey: comprasKeys.requisicion(vars.requisicionId),
      });
    },
  });
}

// ─── Eliminar línea ─────────────────────────────────────────────────

export interface EliminarLineaArgs {
  requisicionId: string;
  lineaId: string;
}

export function useEliminarLinea() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, EliminarLineaArgs>({
    mutationFn: async ({ requisicionId, lineaId }) => {
      await apiRequest<void>(
        `/api/v1/compras/requisiciones/${requisicionId}/lineas/${lineaId}`,
        { method: 'DELETE' },
      );
    },
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({
        queryKey: comprasKeys.requisicion(vars.requisicionId),
      });
    },
  });
}
