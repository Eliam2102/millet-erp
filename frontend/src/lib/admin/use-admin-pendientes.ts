import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import type {
  ListarParametrosResponse,
  ListarSeriesResponse,
} from '@/modules/administracion/api/types';
import { pendientesComprobados, type EquivalenciaResumen } from './pendientes';
import { adminEntryVisible } from './registry';

/** Consultas acotadas, solo con empresa, sucursal (cuando aplica) y permiso. */
export function useAdminPendientes() {
  const {
    permisos,
    currentEmpresaId: empresaId,
    currentSucursalId: sucursalId,
    user,
  } = useAuthStore();
  const scope = [user?.id, empresaId, sucursalId];
  const puedeEquivalencias = permisos.includes(P.CentrosCostoCatalogoLeer);
  const puedeSeries = permisos.includes(P.AdminSeriesGestionar);
  const equivalencias = useQuery({
    queryKey: ['admin', 'pendientes', 'equivalencias', ...scope],
    enabled: !!empresaId && !!sucursalId && puedeEquivalencias,
    queryFn: async ({ signal }) =>
      (
        await apiRequest<EquivalenciaResumen[]>(
          `/api/v1/centros-costo/equivalencias/${sucursalId}`,
          { signal },
        )
      ).data,
    staleTime: 30_000,
  });
  const series = useQuery({
    queryKey: ['admin', 'pendientes', 'series', ...scope],
    enabled: !!empresaId && !!sucursalId && puedeSeries,
    queryFn: async ({ signal }) =>
      (
        await apiRequest<ListarSeriesResponse>(
          `/api/v1/admin/series?empresaId=${empresaId}&sucursalId=${sucursalId}&limit=1`,
          { signal },
        )
      ).data,
    staleTime: 30_000,
  });
  const parametros = useQuery({
    queryKey: ['admin', 'pendientes', 'parametros', user?.id, empresaId],
    enabled: !!empresaId && permisos.includes(P.AdminParametrosLeer),
    queryFn: async ({ signal }) =>
      (await apiRequest<ListarParametrosResponse>('/api/v1/admin/parametros?modulo=', { signal }))
        .data,
    staleTime: 30_000,
  });
  const consultas = [equivalencias, series, parametros];
  return {
    pendientes: pendientesComprobados({
      equivalencias:
        equivalencias.isSuccess && puedeEquivalencias && sucursalId
          ? equivalencias.data
          : undefined,
      series: series.isSuccess && puedeSeries && sucursalId ? series.data : undefined,
      parametros:
        parametros.isSuccess && permisos.includes(P.AdminParametrosLeer)
          ? parametros.data
          : undefined,
    }).filter((item) => adminEntryVisible(item.entry, permisos)),
    cargando: consultas.some((query) => query.isLoading),
    error: consultas.some((query) => query.isError),
    requiereSucursal: !sucursalId && (puedeEquivalencias || puedeSeries),
  };
}
