import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { almacenKeys } from '@/features/almacen/api/keys';
import type {
  AplicarDevolucionInternaCommand,
  AplicarDevolucionInternaResponse,
  BajaPorDanoCommand,
  BajaPorDanoResponse,
  ReincorporacionTrasRevisionCommand,
} from '@/features/almacen/api/types';

/**
 * Hooks de Devoluciones Internas (FE-F4-PR1). Cubre los 3 sub-flujos
 * one-shot del 8.A + MAT-REV (A15):
 *
 * <list type="bullet">
 *   <item><b>Aplicar devolución interna</b>: cantidad de una salida
 *     origen vuelve al sub-almacén destino al costo de la salida.</item>
 *   <item><b>Baja por daño</b> (Calidad): material en MAT-REV →
 *     destrucción.</item>
 *   <item><b>Reincorporar tras revisión</b> (Calidad): MAT-REV →
 *     sub-almacén activo (vuelve al CPP).</item>
 * </list>
 *
 * <para>Los 3 invalidan la bandeja de Salidas (para devolución
 * interna) o la de Recepciones (para reincorporación), porque
 * producen movimientos visibles en esas bandejas según su tipo.
 * Hacemos invalidación amplia (<c>almacenKeys.all</c>) por
 * simplicidad — las bandejas afectadas dependen del flujo y no vale
 * la pena ser quirúrgico.</para>
 */

export function useAplicarDevolucionInterna() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: AplicarDevolucionInternaCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<AplicarDevolucionInternaResponse>(
        '/api/v1/almacen/devoluciones-internas',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.all });
    },
  });
}

export function useBajaPorDano() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: BajaPorDanoCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<BajaPorDanoResponse>(
        '/api/v1/almacen/mat-rev/baja-por-dano',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.all });
    },
  });
}

export function useReincorporarTrasRevision() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: ReincorporacionTrasRevisionCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<BajaPorDanoResponse>(
        '/api/v1/almacen/mat-rev/reincorporar',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.all });
    },
  });
}
