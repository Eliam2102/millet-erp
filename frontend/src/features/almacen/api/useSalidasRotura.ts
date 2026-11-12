import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { almacenKeys } from '@/features/almacen/api/keys';

/** ALM-03: salida por rotura en almacén (espejo de `SalidaRoturaResponse`). */
export interface SalidaRotura {
  id: string;
  sucursalId: string;
  ubicacionId: string;
  ubicacionClave: string | null;
  articuloId: string;
  articuloClave: string | null;
  articuloDescripcion: string | null;
  cantidad: number;
  unidadMedida: string;
  fecha: string;
  motivoCodigo: string;
  motivo: string;
  comentario: string | null;
  estado: 'PENDIENTE' | 'AUTORIZADA' | 'RECHAZADA';
  capturadoPor: string;
  capturadoAt: string;
  decididoPor: string | null;
  decididoAt: string | null;
  motivoRechazo: string | null;
  movimientoId: string | null;
  folioMovimiento: string | null;
  costoUnitarioMxn: number | null;
  valorMxn: number | null;
  /** 86e3zf04y: área (CeCo) y equipo capturados para la póliza de merma. */
  areaClave?: string | null;
  equipoClave?: string | null;
  /** M-223: foto o documento que respalda la rotura (obligatoria al capturar). */
  evidenciaBlobRef?: string | null;
}

export interface MotivoRotura {
  codigo: string;
  texto: string;
}

export interface CapturarRoturaPayload {
  ubicacionId: string;
  articuloId: string;
  cantidad: number;
  fecha: string;
  motivoCodigo: string;
  comentario: string | null;
  areaClave?: string | null;
  equipoClave?: string | null;
  evidenciaBlobRef: string;
}

/** 86e3zf04y: si la cuenta de merma exige área o equipo (regla C-03) para la ubicación y la fecha. */
export interface DimensionesRotura {
  exigeArea: boolean;
  exigeEquipo: boolean;
  cuentaCodigo: string | null;
}

export function useDimensionesRotura(ubicacionId: string | null, fecha: string) {
  return useQuery({
    queryKey: almacenKeys.dimensionesRotura(ubicacionId ?? '', fecha),
    enabled: !!ubicacionId && !!fecha,
    queryFn: async ({ signal }) => {
      const qs = new URLSearchParams({ ubicacionId: ubicacionId!, fecha });
      const { data } = await apiRequest<DimensionesRotura>(`/api/v1/almacen/salidas-rotura/dimensiones?${qs}`, { signal });
      return data;
    },
  });
}

export function useSalidasRotura() {
  return useQuery({
    queryKey: almacenKeys.salidasRotura({}),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<SalidaRotura[]>('/api/v1/almacen/salidas-rotura', { signal });
      return data;
    },
  });
}

export function useMotivosRotura() {
  return useQuery({
    queryKey: almacenKeys.motivosRotura(),
    staleTime: Infinity,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<MotivoRotura[]>('/api/v1/almacen/salidas-rotura/motivos', { signal });
      return data;
    },
  });
}

export function useCapturarRotura() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { command: CapturarRoturaPayload; idempotencyKey: string }) => {
      const { data } = await apiRequest<SalidaRotura>('/api/v1/almacen/salidas-rotura', {
        method: 'POST',
        body: args.command,
        idempotencyKey: args.idempotencyKey,
      });
      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: almacenKeys.all }),
  });
}

export function useDecidirRotura() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { id: string; accion: 'autorizar' | 'rechazar'; motivo?: string }) => {
      const { data } = await apiRequest<SalidaRotura>(`/api/v1/almacen/salidas-rotura/${args.id}/${args.accion}`, {
        method: 'POST',
        body: args.accion === 'rechazar' ? { motivo: args.motivo } : undefined,
        idempotencyKey: crypto.randomUUID(),
      });
      return data;
    },
    // Autorizar baja el saldo: se refrescan también saldos y salidas.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: almacenKeys.all }),
  });
}

/** M-223: ROTURA de la app de Almacenes pendiente de decidir. `payload` es el JSON crudo del mensaje de la app. */
export interface RoturaAppPendiente {
  mensajeId: string;
  referencia: string | null;
  folio: string | null;
  recibidoAt: string;
  payload: string;
}

export function useRoturasApp(habilitado: boolean) {
  return useQuery({
    queryKey: almacenKeys.roturasApp(),
    enabled: habilitado,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<RoturaAppPendiente[]>('/api/v1/almacen/salidas-rotura/app', { signal });
      return data;
    },
  });
}

export function useDecidirRoturaApp() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { mensajeId: string; accion: 'autorizar' | 'rechazar'; motivo?: string }) => {
      const { data } = await apiRequest<{ mensajeId: string; estado: string; folio: string | null }>(
        `/api/v1/almacen/salidas-rotura/app/${encodeURIComponent(args.mensajeId)}/${args.accion}`,
        {
          method: 'POST',
          body: args.accion === 'rechazar' ? { motivo: args.motivo } : undefined,
          idempotencyKey: crypto.randomUUID(),
        },
      );
      return data;
    },
    // Autorizar baja el saldo: se refrescan también saldos y salidas.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: almacenKeys.all }),
  });
}
