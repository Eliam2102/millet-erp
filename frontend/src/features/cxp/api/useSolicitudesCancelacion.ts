import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { cxpKeys } from '@/features/cxp/api/keys';

/** P8.7: solicitudes de cancelación que el proveedor pidió en el portal del SAT. */
export const AreaSolicitudCancelacion = {
  Compras: 1,
  Transportes: 2,
  MateriaPrima: 3,
  ComercioExterior: 4,
} as const;
export type AreaSolicitudCancelacion =
  (typeof AreaSolicitudCancelacion)[keyof typeof AreaSolicitudCancelacion];

export const AreaSolicitudCancelacionLabels: Record<AreaSolicitudCancelacion, string> = {
  1: 'Compras',
  2: 'Transportes',
  3: 'Materia prima',
  4: 'Comercio exterior',
};

export const EstadoSolicitudCancelacion = {
  Pendiente: 1,
  Aceptada: 2,
  Rechazada: 3,
} as const;
export type EstadoSolicitudCancelacion =
  (typeof EstadoSolicitudCancelacion)[keyof typeof EstadoSolicitudCancelacion];

export interface SolicitudCancelacion {
  id: string;
  cfdiRecibidoId: string;
  area: AreaSolicitudCancelacion;
  estado: EstadoSolicitudCancelacion;
  fechaRecepcion: string;
  plazoSatVence: string;
  responsableId: string | null;
  responsableNombre: string | null;
  notas: string | null;
  fechaDecision: string | null;
  decididoPor: string | null;
  decididoPorNombre: string | null;
  comentarioDecision: string | null;
  evidenciaNombre: string | null;
}

const base = (cfdiId: string) =>
  `/api/v1/cuentas-por-pagar/cfdis/${cfdiId}/solicitudes-cancelacion`;

const keySolicitudes = (cfdiId: string) =>
  [...cxpKeys.cfdis(), 'solicitudes-cancelacion', cfdiId] as const;

export function useSolicitudesCancelacion(cfdiId: string | null) {
  return useQuery({
    queryKey: keySolicitudes(cfdiId ?? ''),
    enabled: cfdiId !== null,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<SolicitudCancelacion[]>(base(cfdiId!), { signal });
      return data;
    },
  });
}

export function useRegistrarSolicitudCancelacion(cfdiId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      area: AreaSolicitudCancelacion;
      fechaRecepcion: string | null;
      notas: string | null;
      idempotencyKey: string;
    }) => {
      const { idempotencyKey, ...body } = args;
      const { data } = await apiRequest<SolicitudCancelacion>(base(cfdiId), {
        method: 'POST',
        body,
        idempotencyKey,
      });
      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keySolicitudes(cfdiId) }),
  });
}

export function useDecidirSolicitudCancelacion(cfdiId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      solicitudId: string;
      aceptar: boolean;
      comentario: string;
      archivo: File | null;
      idempotencyKey: string;
    }) => {
      const formData = new FormData();
      formData.append('aceptar', String(args.aceptar));
      formData.append('comentario', args.comentario);
      if (args.archivo) formData.append('archivo', args.archivo);
      const { data } = await apiRequest<SolicitudCancelacion>(
        `${base(cfdiId)}/${args.solicitudId}/decidir`,
        { method: 'POST', body: formData, idempotencyKey: args.idempotencyKey },
      );
      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keySolicitudes(cfdiId) }),
  });
}
