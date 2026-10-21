import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type {
  IncidenciaProveedorOc,
  TipoIncidenciaProveedorOc,
} from '@/features/compras/ordenes/api/types';

export interface RegistrarIncidenciaProveedorCommand {
  lineaOrdenCompraId: string;
  tipo: TipoIncidenciaProveedorOc;
  cantidad: number;
  fechaCompromiso: string;
  responsableId: string;
  descripcion: string | null;
}

/**
 * F1-COM-07: acciones sobre incidencias del proveedor. `accion` arma la ruta:
 * registrar → POST /incidencias-proveedor; reprogramar/cerrar → POST /incidencias-proveedor/{id}/{accion}.
 */
export type AccionIncidenciaArgs = { ordenCompraId: string; idempotencyKey: string } & (
  | { accion: 'registrar'; command: RegistrarIncidenciaProveedorCommand }
  | { accion: 'reprogramar'; incidenciaId: string; command: { fechaCompromiso: string; motivo: string } }
  | { accion: 'cerrar'; incidenciaId: string; command: { motivo: string } }
);

export function useAccionIncidenciaProveedor() {
  const queryClient = useQueryClient();
  return useMutation<IncidenciaProveedorOc, Error, AccionIncidenciaArgs>({
    mutationFn: async (args) => {
      const base = `/api/v1/compras/ordenes/${args.ordenCompraId}/incidencias-proveedor`;
      const url = args.accion === 'registrar' ? base : `${base}/${args.incidenciaId}/${args.accion}`;
      return (
        await apiRequest<IncidenciaProveedorOc>(url, {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        })
      ).data;
    },
    onSuccess: (_data, { ordenCompraId }) => {
      queryClient.invalidateQueries({ queryKey: ordenesKeys.detail(ordenCompraId) });
      queryClient.invalidateQueries({ queryKey: ordenesKeys.all });
    },
  });
}
