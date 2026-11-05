import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { almacenKeys } from '@/features/almacen/api/keys';

/** Aviso dentro del ERP generado por Almacén (espejo de `AvisoAlmacenResponse`). */
export interface AvisoAlmacen {
  id: string;
  tipo: 'vale-vencido' | 'ajuste-mayor' | 'acta-por-revisar';
  titulo: string;
  mensaje: string;
  ruta: string | null;
  creadoAt: string;
  leido: boolean;
}

export function useAvisosAlmacen(habilitado: boolean) {
  return useQuery({
    queryKey: almacenKeys.avisos(),
    enabled: habilitado,
    refetchInterval: 15000,
    queryFn: async ({ signal }) => (await apiRequest<AvisoAlmacen[]>('/api/v1/almacen/avisos', { signal })).data,
  });
}

export function useMarcarAvisoLeido() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await apiRequest<void>(`/api/v1/almacen/avisos/${id}/leer`, { method: 'POST' });
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: almacenKeys.avisos() }),
  });
}

/** Acta del cierre mensual (espejo de `ActaCierreResponse`). */
export interface ActaCierre {
  id: string;
  anio: number;
  mes: number;
  estado: 'POR_REVISAR' | 'REVISADA';
  ejecutadoPorId: string;
  ejecutadoPor: string;
  ejecutadoAt: string;
  revisadaPorId: string | null;
  revisadaPor: string | null;
  revisadaAt: string | null;
  contenido: {
    almacenes: { almacen: string; renglones: number; valorInventarioMxn: number }[];
    conteos: { folio: string; tipo: string; fechaAprobacion: string | null }[];
    vales: { folio: string; fecha: string }[];
  };
}

export function useActasCierre() {
  return useQuery({
    queryKey: almacenKeys.cierreActas(),
    queryFn: async ({ signal }) => (await apiRequest<ActaCierre[]>('/api/v1/almacen/cierre-mes/actas', { signal })).data,
  });
}

export function useRevisarActaCierre() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      (await apiRequest<ActaCierre>(`/api/v1/almacen/cierre-mes/acta/${id}/revisar`, { method: 'POST', idempotencyKey: crypto.randomUUID() })).data,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: almacenKeys.cierreActas() });
      void queryClient.invalidateQueries({ queryKey: almacenKeys.avisos() });
    },
  });
}
