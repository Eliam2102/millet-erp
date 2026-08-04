import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type { OrdenCompraResumen } from '@/features/compras/ordenes/api/types';

export interface ListarHermanasDuplicadasResponse {
  hermanas: OrdenCompraResumen[];
}

/**
 * <c>useOcsHermanasDuplicadas(ocOrigenId)</c> — GET
 * <c>/{id}/duplicadas</c> (UF5-PR2, F7-PR3). Devuelve las OCs cuya
 * <c>oc_origen_id</c> apunta a la OC actual.
 *
 * <para>Útil para el aside list del detalle: si la OC actual es origen
 * de duplicaciones, mostrar la lista de hermanas con link al detalle.
 * Si la OC actual es duplicada (tiene <c>ocOrigenId</c>), el caller
 * usa este hook con <c>ocOrigenId</c> para listar las hermanas que
 * comparten origen.</para>
 *
 * <para><b>Skipping</b>: pasar <c>enabled: false</c> implícito vía
 * <c>id == null</c> ahorra una request cuando la OC no participa de
 * ningún chain de duplicaciones.</para>
 */
export function useOcsHermanasDuplicadas(
  ocOrigenId: string | null | undefined,
) {
  return useQuery({
    queryKey: [
      ...ordenesKeys.all,
      'hermanas-duplicadas',
      ocOrigenId ?? 'none',
    ] as const,
    enabled: ocOrigenId != null,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ListarHermanasDuplicadasResponse>(
        `/api/v1/compras/ordenes/${ocOrigenId}/duplicadas`,
        { signal },
      );
      return data;
    },
  });
}
