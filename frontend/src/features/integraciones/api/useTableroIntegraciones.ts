import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';

/** I13.6: estado de un mensaje de integración (valores del backend). */
export const EstadoMensajeIntegracion = { Pendiente: 1, Reintentando: 2, Error: 3, Ok: 4 } as const;
export type EstadoMensajeIntegracion =
  (typeof EstadoMensajeIntegracion)[keyof typeof EstadoMensajeIntegracion];

export const TipoAlertaIntegracion = {
  MensajesEnError: 1,
  FlujoDetenido: 2,
  ReintentosAltos: 3,
} as const;
export type TipoAlertaIntegracion =
  (typeof TipoAlertaIntegracion)[keyof typeof TipoAlertaIntegracion];

export interface MensajeIntegracion {
  id: string;
  referencia: string;
  estado: EstadoMensajeIntegracion;
  intentos: number;
  ultimoError: string | null;
  creadoEn: string;
  procesadoEn: string | null;
}

export interface FuenteIntegracion {
  clave: string;
  nombre: string;
  grupo: string;
  permiteReintento: boolean;
  disponible: boolean;
  errorLectura: string | null;
  pendientes: number;
  reintentando: number;
  enError: number;
  okUltimas24h: number;
  ultimoExito: string | null;
  pendienteMasAntiguo: string | null;
  detenido: boolean;
  mensajes: MensajeIntegracion[];
}

export interface AlertaIntegracion {
  id: string;
  fuenteClave: string;
  fuenteNombre: string;
  tipo: TipoAlertaIntegracion;
  detalle: string;
  abiertaEn: string;
  notificadaEn: string | null;
  cerradaEn: string | null;
  recuperacionNotificadaEn: string | null;
}

export interface TableroIntegraciones {
  generadoEn: string;
  umbralDetenidoMinutos: number;
  fuentes: FuenteIntegracion[];
  alertasAbiertas: AlertaIntegracion[];
}

const BASE = '/api/v1/integraciones/tablero';
const keys = {
  all: ['integraciones', 'tablero'] as const,
  tablero: () => [...keys.all, 'estado'] as const,
  alertas: () => [...keys.all, 'alertas'] as const,
};

/** Se refresca cada 30 s: es un tablero de operación. */
export function useTableroIntegraciones() {
  return useQuery<TableroIntegraciones>({
    queryKey: keys.tablero(),
    refetchInterval: 30_000,
    queryFn: async ({ signal }) => (await apiRequest<TableroIntegraciones>(BASE, { signal })).data,
  });
}

/** Historial de alertas: se refresca cada 30 s, igual que el tablero, para ver abiertas y recuperadas sin recargar (QA H1). */
export function useAlertasIntegracion() {
  return useQuery<AlertaIntegracion[]>({
    queryKey: keys.alertas(),
    refetchInterval: 30_000,
    queryFn: async ({ signal }) =>
      (await apiRequest<AlertaIntegracion[]>(`${BASE}/alertas?limit=50`, { signal })).data,
  });
}

export function useReintentarMensajeIntegracion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { clave: string; mensajeId: string; idempotencyKey: string }) => {
      await apiRequest<void>(
        `${BASE}/${encodeURIComponent(args.clave)}/mensajes/${encodeURIComponent(args.mensajeId)}/reintentar`,
        { method: 'POST', idempotencyKey: args.idempotencyKey },
      );
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.all }),
  });
}
