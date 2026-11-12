import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { tesoreriaKeys } from './keys';

/** M-372: estados del cheque recibido (espejo de `EstadoChequeRecibido`). */
export const EstadoChequeRecibido = {
  EnFirmePendiente: 1,
  Disponible: 2,
  Rebotado: 3,
} as const;
export type EstadoChequeRecibido = (typeof EstadoChequeRecibido)[keyof typeof EstadoChequeRecibido];

export const ESTADO_CHEQUE_LABELS: Record<EstadoChequeRecibido, string> = {
  [EstadoChequeRecibido.EnFirmePendiente]: 'En firme pendiente',
  [EstadoChequeRecibido.Disponible]: 'Disponible',
  [EstadoChequeRecibido.Rebotado]: 'Rebotado',
};

export interface ChequeRecibidoResponse {
  id: string;
  cuentaBancariaId: string;
  cuenta: string;
  clienteId: string;
  clienteNombre: string | null;
  bancoEmisor: string;
  numero: string;
  librador: string;
  importe: number;
  moneda: string;
  fechaRecepcion: string;
  estado: EstadoChequeRecibido;
  movimientoId: string | null;
  extractoLineaId: string | null;
  confirmadoEn: string | null;
  contramovimientoId: string | null;
  motivoRebote: string | null;
  rebotadoEn: string | null;
}

export interface LineaCandidataCheque {
  conciliacionId: string;
  extractoLineaId: string;
  fecha: string;
  descripcion: string;
  referencia: string | null;
  monto: number;
}

export interface RegistrarChequeCommand {
  cuentaBancariaId: string;
  clienteId: string;
  bancoEmisor: string;
  numero: string;
  librador: string;
  importe: number;
  fechaRecepcion: string;
}

const BASE = '/api/v1/tesoreria/cheques';
const KEY = [...tesoreriaKeys.all, 'cheques'] as const;

export function useChequesRecibidos(estado?: EstadoChequeRecibido) {
  return useQuery<ChequeRecibidoResponse[]>({
    queryKey: [...KEY, estado ?? 'todos'],
    queryFn: async ({ signal }) =>
      (await apiRequest<ChequeRecibidoResponse[]>(estado ? `${BASE}?estado=${estado}` : BASE, { signal })).data,
  });
}

export function useLineasCandidatasCheque(chequeId: string | null) {
  return useQuery<LineaCandidataCheque[]>({
    queryKey: [...KEY, chequeId, 'lineas'],
    enabled: chequeId != null,
    queryFn: async ({ signal }) =>
      (await apiRequest<LineaCandidataCheque[]>(`${BASE}/${chequeId}/lineas-candidatas`, { signal })).data,
  });
}

function useAccionCheque<T>(ruta: (args: T) => { path: string; body: unknown }) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: T & { idempotencyKey: string }) => {
      const { path, body } = ruta(args);
      return (await apiRequest<ChequeRecibidoResponse>(`${BASE}${path}`, {
        method: 'POST',
        body,
        idempotencyKey: args.idempotencyKey,
      })).data;
    },
    // Confirmar crea el ingreso y empareja la línea; rebotar crea el contramovimiento.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: tesoreriaKeys.all }),
  });
}

export function useRegistrarCheque() {
  return useAccionCheque((a: { command: RegistrarChequeCommand }) => ({ path: '', body: a.command }));
}

/** Con `extractoLineaId` se confirma contra el estado de cuenta; sin ella, a mano con fecha y referencia. */
export function useConfirmarCheque() {
  return useAccionCheque(
    (a: { id: string; extractoLineaId?: string; fechaValor?: string; referencia?: string }) => ({
      path: `/${a.id}/confirmar`,
      body: { extractoLineaId: a.extractoLineaId, fechaValor: a.fechaValor, referencia: a.referencia },
    }),
  );
}

export function useRebotarCheque() {
  return useAccionCheque((a: { id: string; fecha: string; motivo: string }) => ({
    path: `/${a.id}/rebotar`,
    body: { fecha: a.fecha, motivo: a.motivo },
  }));
}
