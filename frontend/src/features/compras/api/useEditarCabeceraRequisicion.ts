import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { comprasKeys } from '@/features/compras/api/keys';
import type { Clasificacion, Prioridad } from '@/features/compras/api/types';

/**
 * Body del <c>PATCH /api/v1/compras/requisiciones/{id}</c> (cabecera, solo
 * Borrador). PATCH parcial: lo ausente no se toca; <c>limpiarX</c> vacía un
 * campo opcional.
 */
export interface EditarCabeceraRequisicionCommand {
  descripcion?: string;
  fechaEntregaDeseada?: string;
  prioridad?: Prioridad;
  proveedorSugeridoId?: string;
  clasificacion?: Clasificacion;
  obra?: string;
  soloApartarSiCompleto?: boolean;
  limpiarDescripcion?: boolean;
  limpiarFechaEntregaDeseada?: boolean;
  limpiarProveedorSugeridoId?: boolean;
  limpiarObra?: boolean;
}

export interface EditarCabeceraRequisicionArgs {
  requisicionId: string;
  command: EditarCabeceraRequisicionCommand;
  idempotencyKey: string;
}

export function useEditarCabeceraRequisicion() {
  const queryClient = useQueryClient();
  return useMutation<void, Error, EditarCabeceraRequisicionArgs>({
    mutationFn: async ({ requisicionId, command, idempotencyKey }) => {
      await apiRequest<void>(`/api/v1/compras/requisiciones/${requisicionId}`, {
        method: 'PATCH',
        body: command,
        idempotencyKey,
      });
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: comprasKeys.all }),
  });
}
