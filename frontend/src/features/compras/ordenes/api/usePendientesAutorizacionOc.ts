import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type {
  ListarOrdenesCompraResponse,
  OrdenCompraResumen,
} from '@/features/compras/ordenes/api/types';

/**
 * Nivel de autorización pendiente. Mismo valor que el backend
 * <c>NivelAutorizacion</c> enum (strings); el frontend pasa el valor
 * raw al query string.
 */
export type NivelAutorizacionFiltro = 'Nivel1' | 'Nivel2';

/**
 * Fila de la bandeja de pendientes: resumen de la OC más los indicadores de
 * decisión que calcula el backend (siempre presentes en esta proyección).
 */
export type OrdenPendienteAutorizacion = OrdenCompraResumen & {
  cantidadTotal: number;
  numeroLineas: number;
  totalAPagar: number;
  esMenorCuantia: boolean;
  esRecurrente: boolean;
  proveedorConocido: boolean;
  requiereNivel2: boolean;
};

export type ListarPendientesAutorizacionOcResponse = Omit<
  ListarOrdenesCompraResponse,
  'items'
> & {
  items: OrdenPendienteAutorizacion[];
};

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
    queryKey: ordenesKeys.pendientes(nivel ?? 'todos', page, pageSize),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (nivel) params.set('nivel', nivel);
      params.set('page', String(page));
      params.set('pageSize', String(pageSize));
      const { data } = await apiRequest<ListarPendientesAutorizacionOcResponse>(
        `/api/v1/compras/ordenes/pendientes-autorizacion?${params}`,
        { signal },
      );
      return data;
    },
  });
}
