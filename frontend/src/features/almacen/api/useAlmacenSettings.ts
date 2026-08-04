import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { almacenKeys } from '@/features/almacen/api/keys';

/**
 * Configuración del módulo Almacén de la empresa actual (una fila por
 * empresa, molde ComprasSettings). Hoy un solo setting: el interruptor
 * operativo del motor de reabasto automático (<c>ReordenWorker</c>).
 *
 * <para>El efecto de un cambio NO es inmediato: el worker consulta el flag
 * al inicio de cada ciclo (intervalo por config, default 1 h). Apagar no
 * cancela un barrido en curso; los borradores ya generados quedan vivos
 * como requisiciones normales.</para>
 */
export interface AlmacenSettingsResponse {
  empresaId: string;
  reabastoAutomaticoActivo: boolean;
}

/** GET /api/v1/almacen/configuracion — gate almacen.reorden.leer. */
export function useAlmacenSettings() {
  return useQuery({
    queryKey: almacenKeys.settings(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<AlmacenSettingsResponse>(
        '/api/v1/almacen/configuracion',
        { signal },
      );
      return data;
    },
  });
}

/**
 * PATCH /api/v1/almacen/configuracion — upsert parcial, gate
 * almacen.reorden.administrar. Idempotency-Key por invocación (ADR-0020):
 * el caller la genera al confirmar la acción, no al montar.
 */
export function useActualizarAlmacenSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      reabastoAutomaticoActivo: boolean;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<AlmacenSettingsResponse>(
        '/api/v1/almacen/configuracion',
        {
          method: 'PATCH',
          body: { reabastoAutomaticoActivo: args.reabastoAutomaticoActivo },
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (data) => {
      queryClient.setQueryData(almacenKeys.settings(), data);
    },
  });
}
