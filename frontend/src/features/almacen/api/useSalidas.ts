import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  almacenKeys,
  type ListarSalidasFiltros,
} from '@/features/almacen/api/keys';
import { comprasKeys } from '@/features/compras/api/keys';
import type {
  PagedResponse,
  RegistrarSalidaConRequisicionCommand,
  RegistrarSalidaPorValeCommand,
  RegistrarSalidaResponse,
  RegularizarValeCommand,
  SalidaDetalle,
  SalidaListItem,
} from '@/features/almacen/api/types';

/**
 * Hooks de Salidas (FE-F3-PR1). Cubre la bandeja paginada, el detalle
 * y las dos mutaciones de registro:
 * <list type="bullet">
 *   <item><b>Variante A</b> (<c>useRegistrarSalidaConRq</c>): salida
 *     normal contra una RQ aprobada.</item>
 *   <item><b>Variante B</b> (<c>useRegistrarSalidaPorVale</c>): vale
 *     urgente (A14). Requiere upload previo del vale a
 *     <c>/api/v1/almacen/salidas/vale/blob</c>.</item>
 * </list>
 * Más <c>useRegularizarVale</c> para vincular RQ posterior al vale, y
 * <c>useSubirValeBlob</c> para el upload del archivo.
 */

function buildQuery(filtros: object): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(filtros)) {
    if (value !== undefined && value !== null && value !== '') {
      params.set(key, String(value));
    }
  }
  return params.toString();
}

export function useSalidas(filtros: ListarSalidasFiltros = {}) {
  return useQuery({
    queryKey: almacenKeys.salidasList(filtros),
    queryFn: async ({ signal }) => {
      const query = buildQuery(filtros);
      const path = query
        ? `/api/v1/almacen/salidas?${query}`
        : '/api/v1/almacen/salidas';
      const { data } = await apiRequest<PagedResponse<SalidaListItem>>(
        path,
        { signal },
      );
      return data;
    },
  });
}

export function useSalida(id: string | undefined) {
  return useQuery({
    queryKey: id ? almacenKeys.salidaById(id) : ['almacen', 'salida', 'noop'],
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<SalidaDetalle>(
        `/api/v1/almacen/salidas/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
  });
}

export function useRegistrarSalidaConRq() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: RegistrarSalidaConRequisicionCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<RegistrarSalidaResponse>(
        '/api/v1/almacen/salidas',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (_data, args) => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.salidas() });
      // Invalida el detalle de la RQ para que al reabrir el sheet sobre
      // la misma RQ se vea el `cantSurtidoDeAlmacen` y
      // `cantPendienteSurtir` actualizados (mismo patrón que PR #301
      // hizo para el detalle de OC en recepciones).
      queryClient.invalidateQueries({
        queryKey: comprasKeys.requisicion(args.command.requisicionId),
      });
    },
  });
}

export function useRegistrarSalidaPorVale() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: RegistrarSalidaPorValeCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<RegistrarSalidaResponse>(
        '/api/v1/almacen/salidas/vale',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.salidas() });
    },
  });
}

export function useRegularizarVale() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { command: RegularizarValeCommand }) => {
      await apiRequest<void>(
        `/api/v1/almacen/salidas/${args.command.salidaId}/regularizar`,
        {
          method: 'POST',
          body: { rqRegularizadoraId: args.command.rqRegularizadoraId },
        },
      );
    },
    onSuccess: (_, args) => {
      queryClient.invalidateQueries({
        queryKey: almacenKeys.salidaById(args.command.salidaId),
      });
      queryClient.invalidateQueries({ queryKey: almacenKeys.salidas() });
    },
  });
}

/**
 * Respuesta del endpoint <c>POST /api/v1/almacen/salidas/vale/blob</c>
 * (backend PR #258, paralelo al de packing list). FE sube el archivo
 * primero, recibe la <c>blobRef</c>, y luego la pasa al comando
 * <c>RegistrarSalidaPorValeCommand</c>.
 */
export interface SubirValeResponse {
  blobRef: string;
  nombreArchivo: string;
  tamanoBytes: number;
  contentType: string;
}

/**
 * Hook para subir el vale escaneado/firmado al blob storage de
 * Almacén. Permiso <c>almacen.salidas.por-vale</c>, MIME whitelist,
 * tope 20 MB.
 */
export function useSubirValeBlob() {
  return useMutation({
    mutationFn: async (args: { archivo: File; idempotencyKey: string }) => {
      const formData = new FormData();
      formData.append('archivo', args.archivo);
      const { data } = await apiRequest<SubirValeResponse>(
        '/api/v1/almacen/salidas/vale/blob',
        {
          method: 'POST',
          body: formData,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
  });
}
