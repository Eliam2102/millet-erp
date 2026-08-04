import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type { ListarOrdenesCompraResponse } from '@/features/compras/ordenes/api/types';

/**
 * Nivel de autorización pendiente. Mismo valor que el backend
 * <c>NivelAutorizacion</c> enum (strings); el frontend pasa el valor
 * raw al query string.
 */
export type NivelAutorizacionFiltro = 'Nivel1' | 'Nivel2';

export interface UsePendientesAutorizacionOcOpts {
  /** Si se omite, devuelve ambas bandejas N1+N2 mezcladas. */
  nivel?: NivelAutorizacionFiltro;
  page?: number;
  pageSize?: number;
}

/**
 * <c>usePendientesAutorizacionOc(opts)</c> — bandeja P2 de OCs en
 * <c>EnAutorizacionJefeCompras</c> (Nivel1) o <c>EnAutorizacionDireccion</c>
 * (Nivel2). Orden FIFO (más viejas arriba). Permiso requerido:
 * <c>compras.ordenes.leer</c>.
 *
 * <para>El gateado de "qué bandeja muestro" (N1 / N2 / ambos con tab
 * switcher) lo decide el caller con <c>useHasAnyPermission(['autorizar.nivel1','autorizar.nivel2'])</c>;
 * este hook solo es un wrapper del query del backend.</para>
 */
export function usePendientesAutorizacionOc(
  opts: UsePendientesAutorizacionOcOpts = {},
) {
  const { nivel, page = 1, pageSize = 50 } = opts;
  return useQuery({
    queryKey: ordenesKeys.pendientes(nivel ?? 'todos'),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (nivel) params.set('nivel', nivel);
      params.set('page', String(page));
      params.set('pageSize', String(pageSize));
      const { data } = await apiRequest<ListarOrdenesCompraResponse>(
        `/api/v1/compras/ordenes/pendientes-autorizacion?${params}`,
        { signal },
      );
      return data;
    },
  });
}
