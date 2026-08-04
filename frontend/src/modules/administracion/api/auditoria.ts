import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  adminKeys,
  type ConsultarBitacoraFiltros,
} from '@/modules/administracion/api/keys';
import type { ConsultarBitacoraResponse } from '@/modules/administracion/api/types';

/**
 * Hook de TanStack Query del consolidado de auditoría (F-Admin-PR7.2).
 *
 * <para>Endpoint: <c>GET /api/v1/admin/auditoria</c>. Permiso
 * <c>admin.auditoria.leer</c>. Datos sensibles → <c>staleTime: 0</c>
 * (la query se considera siempre stale; no cacheamos resultados de
 * bitácora). El query se mantiene <c>enabled</c> solo cuando ambos
 * extremos del rango llegan poblados — sin <c>desde</c>/<c>hasta</c>
 * el backend responde 400.</para>
 */
export function useAuditoria(filtros: ConsultarBitacoraFiltros) {
  return useQuery({
    queryKey: adminKeys.auditoriaList(filtros),
    queryFn: async ({ signal }) => {
      const path = buildAuditoriaPath(filtros);
      const { data } = await apiRequest<ConsultarBitacoraResponse>(path, {
        signal,
      });
      return data;
    },
    enabled:
      filtros.desde != null &&
      filtros.desde.length > 0 &&
      filtros.hasta != null &&
      filtros.hasta.length > 0,
    staleTime: 0,
  });
}

function buildAuditoriaPath(filtros: ConsultarBitacoraFiltros): string {
  const params = new URLSearchParams();
  params.set('desde', filtros.desde);
  params.set('hasta', filtros.hasta);
  if (filtros.modulo != null && filtros.modulo.length > 0) {
    params.set('modulo', filtros.modulo);
  }
  if (filtros.recurso != null && filtros.recurso.length > 0) {
    params.set('recurso', filtros.recurso);
  }
  if (filtros.accion != null && filtros.accion.length > 0) {
    params.set('accion', filtros.accion);
  }
  if (filtros.usuarioId != null && filtros.usuarioId.length > 0) {
    params.set('usuarioId', filtros.usuarioId);
  }
  if (filtros.empresaId != null && filtros.empresaId.length > 0) {
    params.set('empresaId', filtros.empresaId);
  }
  if (filtros.offset != null) params.set('offset', String(filtros.offset));
  if (filtros.limit != null) params.set('limit', String(filtros.limit));
  return `/api/v1/admin/auditoria?${params.toString()}`;
}
