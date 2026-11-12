import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { almacenKeys } from '@/features/almacen/api/keys';

/** M-334/M-331: corrección de consumo. Almacén la solicita; Costos la autoriza y se aplica en el mes corriente. */
export interface LineaCorreccion {
  lineaSalidaOrigenId: string;
  cantidad: number;
  ubicacionId: string;
}

export type EstadoCorreccion = 'PENDIENTE' | 'AUTORIZADA' | 'RECHAZADA';

export interface CorreccionConsumo {
  id: string;
  salidaOrigenId: string;
  folioSalida: string;
  tipo: string;
  tipoTexto: string;
  motivo: string;
  observaciones: string | null;
  lineas: LineaCorreccion[];
  cantidad: number;
  estado: EstadoCorreccion;
  solicitadoPor: string;
  solicitadoAt: string;
  decididoPor: string | null;
  decididoAt: string | null;
  motivoRechazo: string | null;
  devolucionId: string | null;
  folioDevolucion: string | null;
}

export interface SaldoLineaCorreccion {
  lineaSalidaId: string;
  articuloClave: string | null;
  articuloDescripcion: string | null;
  unidadMedida: string;
  consumido: number;
  corregido: number;
  neto: number;
  pendienteDeAutorizar: number;
}

export interface CorreccionesSalida {
  salidaId: string;
  folioSalida: string | null;
  consumido: number;
  corregido: number;
  neto: number;
  lineas: SaldoLineaCorreccion[];
  correcciones: CorreccionConsumo[];
}

export interface TipoCorreccion {
  codigo: string;
  texto: string;
}

export interface SolicitarCorreccionCommand {
  salidaOrigenId: string;
  subAlmacenDestinoId: string;
  tipo: string;
  motivo: string;
  observaciones: string | null;
  lineas: LineaCorreccion[];
}

const claves = {
  salida: (id: string) => [...almacenKeys.all, 'correcciones-consumo', 'salida', id] as const,
  lista: (estado?: string) => [...almacenKeys.all, 'correcciones-consumo', 'lista', estado ?? ''] as const,
  tipos: [...almacenKeys.all, 'correcciones-consumo', 'tipos'] as const,
};

export function useCorreccionesSalida(salidaId: string | undefined, enabled = true) {
  return useQuery({
    enabled: enabled && !!salidaId,
    queryKey: claves.salida(salidaId ?? ''),
    queryFn: async ({ signal }) =>
      (await apiRequest<CorreccionesSalida>(`/api/v1/almacen/salidas/${salidaId}/correcciones`, { signal })).data,
  });
}

export function useCorreccionesConsumo(estado?: EstadoCorreccion, enabled = true) {
  return useQuery({
    enabled,
    queryKey: claves.lista(estado),
    queryFn: async ({ signal }) =>
      (
        await apiRequest<CorreccionConsumo[]>(
          `/api/v1/almacen/correcciones-consumo${estado ? `?estado=${estado}` : ''}`,
          { signal },
        )
      ).data,
  });
}

export function useTiposCorreccion(enabled = true) {
  return useQuery({
    enabled,
    queryKey: claves.tipos,
    staleTime: Infinity,
    queryFn: async ({ signal }) =>
      (await apiRequest<TipoCorreccion[]>('/api/v1/almacen/correcciones-consumo/tipos', { signal })).data,
  });
}

function useAccion<TArgs>(fn: (args: TArgs) => Promise<CorreccionConsumo>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: almacenKeys.all }),
  });
}

export function useSolicitarCorreccion() {
  return useAccion(async (args: { command: SolicitarCorreccionCommand; idempotencyKey: string }) =>
    (
      await apiRequest<CorreccionConsumo>('/api/v1/almacen/correcciones-consumo', {
        method: 'POST',
        body: args.command,
        idempotencyKey: args.idempotencyKey,
      })
    ).data,
  );
}

/** Costos autoriza (se aplica la devolución en el mes corriente) o rechaza con motivo. */
export function useDecidirCorreccion() {
  return useAccion(async (args: { id: string; accion: 'autorizar' | 'rechazar'; motivo?: string }) =>
    (
      await apiRequest<CorreccionConsumo>(`/api/v1/almacen/correcciones-consumo/${args.id}/${args.accion}`, {
        method: 'POST',
        body: args.accion === 'rechazar' ? { motivo: args.motivo } : undefined,
        idempotencyKey: crypto.randomUUID(),
      })
    ).data,
  );
}
