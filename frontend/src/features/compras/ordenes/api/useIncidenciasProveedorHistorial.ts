import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type { TipoIncidenciaProveedorOc } from '@/features/compras/ordenes/api/types';

/** F1-COM-07: renglón del historial de incidencias del proveedor. Mirror de <c>IncidenciaHistorialItem</c>. */
export interface IncidenciaHistorialItem {
  id: string;
  ordenCompraId: string;
  folio: string;
  proveedorId: string;
  partida: number;
  tipo: TipoIncidenciaProveedorOc;
  cantidad: number;
  fechaCompromisoInicial: string;
  fechaCompromisoVigente: string;
  reprogramaciones: number;
  abierta: boolean;
  vencida: boolean;
  /** null si sigue abierta o se cerró a mano. */
  cumplidaATiempo: boolean | null;
  responsableNombre: string | null;
  registradaEn: string;
  cerradaEn: string | null;
  motivoCierre: string | null;
}

export interface ListarIncidenciasProveedorResponse {
  items: IncidenciaHistorialItem[];
  resumen: {
    total: number;
    abiertas: number;
    vencidas: number;
    cumplidasATiempo: number;
    cumplidasTarde: number;
  };
  page: number;
  pageSize: number;
  totalCount: number;
}

/** F1-COM-07: <c>GET /api/v1/compras/ordenes/incidencias-proveedor</c> (permiso del reporte de partidas abiertas). */
export function useIncidenciasProveedorHistorial(proveedorId: string | undefined) {
  return useQuery({
    queryKey: [...ordenesKeys.all, 'incidencias-proveedor', proveedorId] as const,
    enabled: proveedorId != null,
    queryFn: async ({ signal }) =>
      (
        await apiRequest<ListarIncidenciasProveedorResponse>(
          `/api/v1/compras/ordenes/incidencias-proveedor?proveedorId=${proveedorId}&pageSize=200`,
          { signal },
        )
      ).data,
  });
}
