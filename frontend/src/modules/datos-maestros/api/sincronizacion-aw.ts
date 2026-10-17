import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

export interface CampoDuenoAw { entidad: string; campo: string; dueno: 'AW' | 'ERP'; siDifiere: string }
export interface MatrizAw { version: string; campos: CampoDuenoAw[] }
export type TipoCambioAw = 'Aplicado' | 'EditadoEnErp' | 'RfcCompartido' | 'Baja' | 'Reactivacion';
export type EstadoCambioAw = 'Informativo' | 'Pendiente' | 'Resuelto';
export interface CambioMaestroAw {
  id: string;
  tipoEntidad: string;
  entidadId: string | null;
  referenciaExterna: string;
  campo: string;
  valorAntes: string | null;
  valorDespues: string | null;
  dueno: 'AW' | 'ERP';
  tipo: TipoCambioAw;
  estado: EstadoCambioAw;
  causa: string;
  versionEvento: string;
  versionMatriz: string;
  ejecucionId: string | null;
  ocurridoEnUtc: string;
  resolucion: string | null;
  resueltoEnUtc: string | null;
  version: number;
}

export const SINCRONIZACION_AW_KEY = ['datos-maestros', 'sincronizacion-aw'] as const;
const base = '/api/v1/datos-maestros';

export function useMatrizAw() {
  return useQuery({
    queryKey: [...SINCRONIZACION_AW_KEY, 'matriz'],
    queryFn: async ({ signal }) => (await apiRequest<MatrizAw>(`${base}/sincronizacion-aw/matriz`, { signal })).data,
  });
}

export function useCambiosAwCliente(id: string | null, enabled: boolean) {
  return useQuery({
    queryKey: [...SINCRONIZACION_AW_KEY, 'cambios', 'Cliente', id],
    queryFn: async ({ signal }) =>
      (await apiRequest<CambioMaestroAw[]>(`${base}/clientes/${id}/cambios-aw`, { signal })).data,
    enabled: enabled && id != null,
  });
}

export type EntidadConflictoAw = 'clientes' | 'productos-aw';
export type FiltroEstadoAw = 'Pendiente' | 'Resuelto' | 'Todos';

export function useConflictosAw(entidad: EntidadConflictoAw, estado: FiltroEstadoAw, enabled: boolean) {
  return useQuery({
    queryKey: [...SINCRONIZACION_AW_KEY, 'conflictos', entidad, estado],
    queryFn: async ({ signal }) =>
      (await apiRequest<CambioMaestroAw[]>(`${base}/${entidad}/conflictos-aw?estado=${estado}`, { signal })).data,
    enabled,
  });
}

export function useResolverConflictoAw() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ entidad, id, resolucion, version }: {
      entidad: EntidadConflictoAw; id: string; resolucion: string; version: number;
    }) => (await apiRequest<void>(`${base}/${entidad}/conflictos-aw/${id}/resolver`, {
      method: 'POST', body: { resolucion, version }, idempotencyKey: crypto.randomUUID(),
    })).data,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: SINCRONIZACION_AW_KEY }),
  });
}

type Variante = 'info' | 'warning' | 'success' | 'danger';
export const TIPO_CAMBIO_AW: Record<TipoCambioAw, { texto: string; variante: Variante }> = {
  Aplicado: { texto: 'Aplicado', variante: 'info' },
  Baja: { texto: 'Baja', variante: 'warning' },
  Reactivacion: { texto: 'Reactivación', variante: 'success' },
  EditadoEnErp: { texto: 'Editado en ERP', variante: 'warning' },
  RfcCompartido: { texto: 'RFC repetido', variante: 'danger' },
};
