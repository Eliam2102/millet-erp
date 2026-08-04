import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  almacenKeys,
  type ListarRecepcionesFiltros,
} from '@/features/almacen/api/keys';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import type {
  PagedResponse,
  RecepcionDetalle,
  RecepcionListItem,
  RegistrarRecepcionConFacturaCommand,
  RegistrarRecepcionConPackingListCommand,
  RegistrarRecepcionResponse,
} from '@/features/almacen/api/types';

/**
 * Hooks de Recepciones (FE-F2-PR1). Cubre la bandeja paginada, el
 * detalle (con líneas inline) y las dos mutaciones de registro:
 * Variante A (factura/CFDI conocido) y Variante B (packing list,
 * factura pendiente). Ambas mutaciones invalidan
 * <c>almacenKeys.recepciones()</c> para refrescar la bandeja y, si
 * aplica, el detalle.
 *
 * <para>POST de registro lleva Idempotency-Key (ADR-0020); el backend
 * lo exige con <c>RequireIdempotencyKeyAttribute</c>.</para>
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

export function useRecepciones(filtros: ListarRecepcionesFiltros = {}) {
  return useQuery({
    queryKey: almacenKeys.recepcionesList(filtros),
    queryFn: async ({ signal }) => {
      const query = buildQuery(filtros);
      const path = query
        ? `/api/v1/almacen/recepciones?${query}`
        : '/api/v1/almacen/recepciones';
      const { data } = await apiRequest<PagedResponse<RecepcionListItem>>(
        path,
        { signal },
      );
      return data;
    },
  });
}

export function useRecepcion(id: string | undefined) {
  return useQuery({
    queryKey: id ? almacenKeys.recepcionById(id) : ['almacen', 'recepcion', 'noop'],
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<RecepcionDetalle>(
        `/api/v1/almacen/recepciones/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
  });
}

export function useRegistrarRecepcionFactura() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: RegistrarRecepcionConFacturaCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<RegistrarRecepcionResponse>(
        '/api/v1/almacen/recepciones',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (_data, args) => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.recepciones() });
      // Invalida el detalle de la OC para que el sheet "Nueva recepción"
      // refleje el nuevo `cantidadRecibida` por línea (no quede el sheet
      // mostrando pendiente viejo al reabrir sobre la misma OC).
      queryClient.invalidateQueries({
        queryKey: ordenesKeys.detail(args.command.ordenCompraId),
      });
    },
  });
}

export function useRegistrarRecepcionPackingList() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: RegistrarRecepcionConPackingListCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<RegistrarRecepcionResponse>(
        '/api/v1/almacen/recepciones/packing-list',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (_data, args) => {
      queryClient.invalidateQueries({ queryKey: almacenKeys.recepciones() });
      // Invalida el detalle de la OC para que el sheet "Nueva recepción"
      // refleje el nuevo `cantidadRecibida` por línea (no quede el sheet
      // mostrando pendiente viejo al reabrir sobre la misma OC).
      queryClient.invalidateQueries({
        queryKey: ordenesKeys.detail(args.command.ordenCompraId),
      });
    },
  });
}

/**
 * Respuesta del endpoint <c>POST /api/v1/almacen/recepciones/packing-list/blob</c>
 * (backend PR #256). El FE sube el archivo primero y recibe
 * <c>blobRef</c>, que luego pasa al comando
 * <c>RegistrarRecepcionConPackingListCommand</c>.
 */
export interface SubirPackingListResponse {
  blobRef: string;
  nombreArchivo: string;
  tamanoBytes: number;
  contentType: string;
}

/**
 * Hook para subir el archivo del packing list al blob storage del
 * módulo Almacén. Backend valida MIME whitelist (PDF + imágenes +
 * Office + texto), tope 20 MB e Idempotency-Key.
 *
 * <para>Flujo de dos pasos: 1) subir archivo, 2) llamar al registro
 * con <c>packingListBlobRef = blobRef</c>. Separar el upload del
 * registro evita multipart en el endpoint transaccional + permite
 * reusar el blob si la validación de la recepción falla.</para>
 */
export function useSubirPackingListBlob() {
  return useMutation({
    mutationFn: async (args: { archivo: File; idempotencyKey: string }) => {
      const formData = new FormData();
      formData.append('archivo', args.archivo);
      const { data } = await apiRequest<SubirPackingListResponse>(
        '/api/v1/almacen/recepciones/packing-list/blob',
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
