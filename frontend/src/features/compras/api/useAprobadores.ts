import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  comprasKeys,
  type ListarAprobadoresVigentesFiltros,
  type ListarAprobadoresHistoricoFiltros,
} from '@/features/compras/api/keys';
import type {
  AprobadorVigenteResponse,
  AprobadorHistoricoResponse,
  DesignarAprobadorResponse,
} from '@/features/compras/api/types';
import type { DesignarAprobadorValues } from '@/features/compras/schemas/designar-aprobador';

/**
 * Hooks de Aprobadores (P9 — UF6-PR1, doc 05 §11.4).
 *
 * <para>Convención de invalidación: tras designar / revocar, invalidar
 * <c>comprasKeys.aprobadores()</c> (familia entera) — refresca tanto
 * "Vigentes" como "Histórico" sin necesidad de saber filtros activos.</para>
 */

function buildFiltrosQuery(
  filtros:
    | ListarAprobadoresVigentesFiltros
    | ListarAprobadoresHistoricoFiltros,
): string {
  const params = new URLSearchParams();
  if (filtros.departamentoId)
    params.set('departamentoId', filtros.departamentoId);
  if (filtros.rol != null) params.set('rol', String(filtros.rol));
  if (filtros.usuarioId) params.set('usuarioId', filtros.usuarioId);
  return params.toString();
}

// ─── Listar vigentes ──────────────────────────────────────────────

/**
 * <c>useAprobadoresVigentes(filtros)</c> — matriz actual de
 * aprobadores. Doc 05 §11.4. Filtros opcionales (depto, rol, usuario);
 * sin paginación (la matriz completa cabe holgadamente).
 */
export function useAprobadoresVigentes(
  filtros: ListarAprobadoresVigentesFiltros = {},
) {
  return useQuery({
    queryKey: comprasKeys.aprobadoresVigentes(filtros),
    queryFn: async ({ signal }) => {
      const query = buildFiltrosQuery(filtros);
      const path = query
        ? `/api/v1/compras/aprobadores?${query}`
        : '/api/v1/compras/aprobadores';
      const { data } = await apiRequest<AprobadorVigenteResponse[]>(path, {
        signal,
      });
      return data;
    },
  });
}

// ─── Listar histórico ─────────────────────────────────────────────

/**
 * <c>useAprobadoresHistorico(filtros)</c> — vigencias actuales y
 * cerradas. Doc 05 §11.4 + §13.4 (UI con form que exige al menos
 * un filtro).
 *
 * <para><b>Filtro obligatorio</b>: el backend devuelve
 * <c>422 FILTRO_OBLIGATORIO</c> si todos los filtros vienen vacíos. El
 * <c>enabled</c> del hook lo gateaclient-side para evitar el roundtrip
 * cuando la UI sabe que no hay ningún filtro seleccionado.</para>
 */
export function useAprobadoresHistorico(
  filtros: ListarAprobadoresHistoricoFiltros,
) {
  const tieneAlgunFiltro =
    filtros.departamentoId != null ||
    filtros.rol != null ||
    filtros.usuarioId != null;

  return useQuery({
    queryKey: comprasKeys.aprobadoresHistorico(filtros),
    queryFn: async ({ signal }) => {
      const query = buildFiltrosQuery(filtros);
      const { data } = await apiRequest<AprobadorHistoricoResponse[]>(
        `/api/v1/compras/aprobadores/historico?${query}`,
        { signal },
      );
      return data;
    },
    enabled: tieneAlgunFiltro,
  });
}

// ─── Designar ─────────────────────────────────────────────────────

export interface DesignarArgs {
  values: DesignarAprobadorValues;
  idempotencyKey: string;
}

/**
 * <c>useDesignarAprobador()</c> — POST con <c>Idempotency-Key</c>
 * (ADR-0020). El backend re-designar al mismo usuario en
 * <c>(depto, rol)</c> es no-op idempotente (devuelve la vigente actual).
 */
export function useDesignarAprobador() {
  const queryClient = useQueryClient();
  return useMutation<DesignarAprobadorResponse, Error, DesignarArgs>({
    mutationFn: async ({ values, idempotencyKey }) => {
      const { data } = await apiRequest<DesignarAprobadorResponse>(
        '/api/v1/compras/aprobadores',
        { method: 'POST', body: values, idempotencyKey },
      );
      return data;
    },
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: comprasKeys.aprobadores() }),
  });
}

// ─── Revocar ──────────────────────────────────────────────────────

export interface RevocarArgs {
  aprobadorId: string;
  idempotencyKey: string;
}

/**
 * <c>useRevocarAprobador()</c> — DELETE con <c>Idempotency-Key</c>.
 * Cierra la vigencia (<c>vigenteHasta = now</c>) — no borra el row;
 * el histórico queda intacto.
 */
export function useRevocarAprobador() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, RevocarArgs>({
    mutationFn: async ({ aprobadorId, idempotencyKey }) => {
      await apiRequest<void>(
        `/api/v1/compras/aprobadores/${aprobadorId}`,
        { method: 'DELETE', idempotencyKey },
      );
    },
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: comprasKeys.aprobadores() }),
  });
}
