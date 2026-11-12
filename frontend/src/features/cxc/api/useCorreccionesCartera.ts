import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { cxcKeys } from '@/features/cxc/api/keys';

const BASE = '/api/v1/cuentas-por-cobrar/cartera';

/** M-237: espejo de <c>CorreccionCarteraResponse</c> (backend). */
export interface CorreccionCarteraItem {
  id: string;
  facturaCarteraId: string;
  folio: string;
  tipo: 'Plazo' | 'ReversaMovimiento';
  estado: 'Pendiente' | 'Aplicada' | 'Rechazada';
  motivo: string;
  solicitadaPorId: string;
  solicitadaPorNombre: string | null;
  solicitadaEn: string;
  decididaPorId: string | null;
  decididaPorNombre: string | null;
  decididaEn: string | null;
  motivoDecision: string | null;
  plazoAnteriorDias: number | null;
  plazoNuevoDias: number | null;
  vencimientoAnterior: string | null;
  vencimientoNuevo: string | null;
  movimientoCarteraId: string | null;
  movimientoTipo: string | null;
  movimientoImporte: number | null;
}

const clavesCorrecciones = (clienteId: string) => [...cxcKeys.estadoCuenta(clienteId), 'correcciones'] as const;

/** Solicitudes de reversa pendientes del cliente (las autoriza otra persona). */
export function useCorreccionesCartera(clienteId: string | null, estado?: CorreccionCarteraItem['estado']) {
  return useQuery<CorreccionCarteraItem[]>({
    queryKey: [...clavesCorrecciones(clienteId ?? ''), estado ?? 'todas'],
    enabled: clienteId != null,
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams({ clienteId: clienteId! });
      if (estado) params.set('estado', estado);
      const { data } = await apiRequest<CorreccionCarteraItem[]>(`${BASE}/correcciones?${params}`, { signal });
      return data;
    },
  });
}

function useCorreccion<TArgs>(clienteId: string, ruta: (a: TArgs) => string, cuerpo?: (a: TArgs) => unknown) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: TArgs) => {
      const { data } = await apiRequest<CorreccionCarteraItem>(ruta(args), {
        method: 'POST',
        body: cuerpo ? JSON.stringify(cuerpo(args)) : undefined,
        idempotencyKey: crypto.randomUUID(),
      });
      return data;
    },
    onSettled: () => {
      // El estado de cuenta, las solicitudes y la antigüedad cambian con cualquier corrección.
      void queryClient.invalidateQueries({ queryKey: cxcKeys.estadoCuenta(clienteId) });
      void queryClient.invalidateQueries({ queryKey: cxcKeys.cartera() });
    },
  });
}

export function useCorregirPlazo(clienteId: string) {
  return useCorreccion<{ facturaCarteraId: string; plazoDias: number; motivo: string }>(
    clienteId,
    (a) => `${BASE}/facturas/${a.facturaCarteraId}/corregir-plazo`,
    (a) => ({ plazoDias: a.plazoDias, motivo: a.motivo }),
  );
}

export function useSolicitarReversaMovimiento(clienteId: string) {
  return useCorreccion<{ movimientoId: string; motivo: string }>(
    clienteId,
    (a) => `${BASE}/movimientos/${a.movimientoId}/solicitar-reversa`,
    (a) => ({ motivo: a.motivo }),
  );
}

export function useAutorizarReversaMovimiento(clienteId: string) {
  return useCorreccion<{ id: string }>(clienteId, (a) => `${BASE}/correcciones/${a.id}/autorizar`);
}

export function useRechazarReversaMovimiento(clienteId: string) {
  return useCorreccion<{ id: string; motivo: string }>(
    clienteId,
    (a) => `${BASE}/correcciones/${a.id}/rechazar`,
    (a) => ({ motivo: a.motivo }),
  );
}
