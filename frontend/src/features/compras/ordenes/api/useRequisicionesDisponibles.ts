import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';

/**
 * Item del endpoint `GET /api/v1/compras/ordenes/requisiciones-disponibles`
 * (mirror de <c>RequisicionDisponibleResponse</c> backend, F4-PR1).
 *
 * <para>Shape mínimo para el selector multi-select del modo
 * Consolidación del Sheet "Nueva OC" — folio + fechas + IDs +
 * count de líneas. Sin las líneas en sí (eso requeriría fetch
 * separado al detalle de la RQ).</para>
 */
export interface RequisicionDisponible {
  id: string;
  folio: string;
  folioAnio: number;
  departamentoId: string;
  requisitanteId: string;
  /** ISO 8601 UTC. */
  fechaSolicitud: string;
  /** ISO date (YYYY-MM-DD), opcional. */
  fechaEntregaDeseada: string | null;
  totalLineas: number;
  proveedorSugeridoId: string | null;
}

/**
 * <c>useRequisicionesDisponibles(sucursalId)</c> — lista RQs en estado
 * Autorizada que NO están comprometidas en otra OC, filtradas por
 * sucursal (restricción §10.5 cerrada del mapa funcional: una OC solo
 * puede consolidar RQs de la misma sucursal destino).
 *
 * <para>Permiso: <c>compras.ordenes.crear</c>.</para>
 *
 * <para><c>enabled: sucursalId != null</c> — si el caller (Sheet
 * "Nueva OC") aún no ha seleccionado sucursal, el hook se inhabilita.
 * Forzar el filtro server-side previene que el usuario abra el
 * selector sin sucursal y vea RQs de todas (que rompería la
 * restricción al consolidar).</para>
 */
export function useRequisicionesDisponibles(
  sucursalId: string | null | undefined,
) {
  return useQuery({
    queryKey: [
      ...ordenesKeys.all,
      'requisiciones-disponibles',
      sucursalId ?? 'noop',
    ] as const,
    queryFn: async ({ signal }) => {
      if (sucursalId == null || sucursalId.length === 0) {
        // Defensa en profundidad — el enabled debería evitar este path.
        throw new Error('useRequisicionesDisponibles invocado sin sucursalId');
      }
      const params = new URLSearchParams({ sucursalId });
      const { data } = await apiRequest<RequisicionDisponible[]>(
        `/api/v1/compras/ordenes/requisiciones-disponibles?${params}`,
        { signal },
      );
      return data;
    },
    enabled: sucursalId != null && sucursalId.length > 0,
    staleTime: 30_000,
  });
}
