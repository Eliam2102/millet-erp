import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { almacenKeys } from '@/features/almacen/api/keys';

export type OrigenCaptura = 'ERP' | 'APP' | 'AMBOS';

export interface EventoOrigenCaptura {
  evento: string;
  nombre: string;
  origen: OrigenCaptura;
  desde: string | null;
  pendientes: { mensajesEnRevision: number; mensajesSinAcuse: number | null };
}

export interface CambioOrigenCaptura {
  id: string;
  evento: string;
  origenAnterior: OrigenCaptura | null;
  origen: OrigenCaptura;
  motivo: string;
  cambiadoPor: string;
  cambiadoAt: string;
}

export interface OrigenCapturaMpResponse {
  eventos: EventoOrigenCaptura[];
  bitacora: CambioOrigenCaptura[];
}

const URL = '/api/v1/almacen/origen-captura-mp';

export function useOrigenCapturaMp() {
  return useQuery({
    queryKey: almacenKeys.origenCapturaMp(),
    queryFn: async ({ signal }) => (await apiRequest<OrigenCapturaMpResponse>(URL, { signal })).data,
  });
}

export function useCambiarOrigenCapturaMp() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { evento: string; origen: 'ERP' | 'APP'; motivo: string }) =>
      (
        await apiRequest<OrigenCapturaMpResponse>(`${URL}/${args.evento}`, {
          method: 'POST',
          body: { origen: args.origen, motivo: args.motivo },
        })
      ).data,
    onSuccess: (data) => queryClient.setQueryData(almacenKeys.origenCapturaMp(), data),
  });
}
