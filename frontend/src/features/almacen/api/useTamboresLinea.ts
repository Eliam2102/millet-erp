import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { almacenKeys } from '@/features/almacen/api/keys';

/** M-172: tambor o cubeta surtido a una línea (espejo de `TamborDto`). */
export interface TamborLinea {
  id: string;
  articuloId: string;
  articuloClave: string | null;
  articuloNombre: string | null;
  estado: 'ACTIVO' | 'GASTADO';
  kgPresentacion: number | null;
  fechaInicio: string;
  fechaFin: string | null;
  movimientoId: string;
  folio: string;
  folioReemplazo: string | null;
}

/** Línea de producción con sus tambores, el más reciente primero (espejo de `LineaTamboresDto`). */
export interface LineaTambores {
  id: string;
  sucursalId: string;
  clave: string;
  nombre: string;
  proceso: string;
  tambores: TamborLinea[];
}

/** Líneas de producción de las sucursales del usuario con su historial de tambores. */
export function useTamboresLinea(opts: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey: almacenKeys.tamboresLinea(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<LineaTambores[]>('/api/v1/almacen/tambores-linea', { signal });
      return data;
    },
    enabled: opts.enabled ?? true,
  });
}
