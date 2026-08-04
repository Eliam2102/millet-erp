import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import {
  almacenKeys,
  type ListarDevolucionesProveedorFiltros,
} from '@/features/almacen/api/keys';
import type {
  AdjuntarEvidenciaDevolucionAProveedorCommand,
  DevolucionProveedorDetalle,
  DevolucionProveedorListItem,
  IniciarDevolucionAProveedorCommand,
  IniciarDevolucionAProveedorResponse,
  PagedResponse,
  RegistrarSalidaDevolucionAProveedorCommand,
  RegistrarSalidaDevolucionAProveedorResponse,
} from '@/features/almacen/api/types';

/**
 * Hooks del sub-flujo 8.B (Devolución a proveedor, FE-F4-PR1). Cubre
 * la state machine completa:
 *
 * <list type="bullet">
 *   <item><c>useDevolucionesProveedor</c>, <c>useDevolucionProveedor</c>:
 *     bandeja + detalle.</item>
 *   <item><c>useIniciarDevolucionProveedor</c>: crea en Borrador.</item>
 *   <item><c>useAdjuntarEvidenciaProveedor</c>: agrega evidencia (al
 *     menos una requerida antes de pasar a EnAutorizacion).</item>
 *   <item><c>useSolicitarAutorizacionProveedor</c>: Borrador → EnAutorizacion.</item>
 *   <item><c>useAutorizarDevolucionProveedor</c>: Dirección autoriza.</item>
 *   <item><c>useRechazarDevolucionProveedor</c>: Dirección rechaza con
 *     motivo.</item>
 *   <item><c>useRegistrarSalidaDevolucionProveedor</c>: Almacenista
 *     registra la salida física (publica el evento al outbox).</item>
 *   <item><c>useSubirEvidenciaBlob</c>: upload del archivo de
 *     evidencia (paralelo a packing list/vale, backend PR #260).</item>
 * </list>
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

export function useDevolucionesProveedor(
  filtros: ListarDevolucionesProveedorFiltros = {},
) {
  return useQuery({
    queryKey: almacenKeys.devolucionesProveedorList(filtros),
    queryFn: async ({ signal }) => {
      const query = buildQuery(filtros);
      const path = query
        ? `/api/v1/almacen/devoluciones-proveedor?${query}`
        : '/api/v1/almacen/devoluciones-proveedor';
      const { data } = await apiRequest<
        PagedResponse<DevolucionProveedorListItem>
      >(path, { signal });
      return data;
    },
  });
}

export function useDevolucionProveedor(id: string | undefined) {
  return useQuery({
    queryKey: id
      ? almacenKeys.devolucionProveedorById(id)
      : ['almacen', 'devolucion-proveedor', 'noop'],
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<DevolucionProveedorDetalle>(
        `/api/v1/almacen/devoluciones-proveedor/${id}`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
  });
}

export function useIniciarDevolucionProveedor() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: IniciarDevolucionAProveedorCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<IniciarDevolucionAProveedorResponse>(
        '/api/v1/almacen/devoluciones-proveedor',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: almacenKeys.devolucionesProveedor(),
      });
    },
  });
}

export function useAdjuntarEvidenciaProveedor() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: AdjuntarEvidenciaDevolucionAProveedorCommand;
    }) => {
      const c = args.command;
      await apiRequest<void>(
        `/api/v1/almacen/devoluciones-proveedor/${c.devolucionId}/evidencias`,
        {
          method: 'POST',
          body: {
            tipoEvidencia: c.tipoEvidencia,
            nombreArchivo: c.nombreArchivo,
            blobRef: c.blobRef,
            comentario: c.comentario,
          },
        },
      );
    },
    onSuccess: (_, args) => {
      queryClient.invalidateQueries({
        queryKey: almacenKeys.devolucionProveedorById(args.command.devolucionId),
      });
    },
  });
}

export function useSolicitarAutorizacionProveedor() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { devolucionId: string }) => {
      await apiRequest<void>(
        `/api/v1/almacen/devoluciones-proveedor/${args.devolucionId}/solicitar-autorizacion`,
        { method: 'POST', body: {} },
      );
    },
    onSuccess: (_, args) => {
      queryClient.invalidateQueries({
        queryKey: almacenKeys.devolucionesProveedor(),
      });
      queryClient.invalidateQueries({
        queryKey: almacenKeys.devolucionProveedorById(args.devolucionId),
      });
    },
  });
}

export function useAutorizarDevolucionProveedor() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { devolucionId: string }) => {
      await apiRequest<void>(
        `/api/v1/almacen/devoluciones-proveedor/${args.devolucionId}/autorizar`,
        { method: 'POST', body: {} },
      );
    },
    onSuccess: (_, args) => {
      queryClient.invalidateQueries({
        queryKey: almacenKeys.devolucionesProveedor(),
      });
      queryClient.invalidateQueries({
        queryKey: almacenKeys.devolucionProveedorById(args.devolucionId),
      });
    },
  });
}

export function useRechazarDevolucionProveedor() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      devolucionId: string;
      motivoRechazo: string;
    }) => {
      await apiRequest<void>(
        `/api/v1/almacen/devoluciones-proveedor/${args.devolucionId}/rechazar`,
        {
          method: 'POST',
          body: { motivoRechazo: args.motivoRechazo },
        },
      );
    },
    onSuccess: (_, args) => {
      queryClient.invalidateQueries({
        queryKey: almacenKeys.devolucionesProveedor(),
      });
      queryClient.invalidateQueries({
        queryKey: almacenKeys.devolucionProveedorById(args.devolucionId),
      });
    },
  });
}

export function useRegistrarSalidaDevolucionProveedor() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: RegistrarSalidaDevolucionAProveedorCommand;
      idempotencyKey: string;
    }) => {
      const c = args.command;
      const { data } = await apiRequest<
        RegistrarSalidaDevolucionAProveedorResponse
      >(
        `/api/v1/almacen/devoluciones-proveedor/${c.devolucionId}/registrar-salida`,
        {
          method: 'POST',
          body: {
            subAlmacenId: c.subAlmacenId,
            fechaMovimiento: c.fechaMovimiento,
            // GAP-4: sin los bins el backend drena la ÚNICA y truena con
            // SALDO_INEXISTENTE cuando el stock vive en un rack.
            bins: c.bins,
          },
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (_, args) => {
      queryClient.invalidateQueries({
        queryKey: almacenKeys.devolucionesProveedor(),
      });
      queryClient.invalidateQueries({
        queryKey: almacenKeys.devolucionProveedorById(args.command.devolucionId),
      });
    },
  });
}

/**
 * Respuesta del endpoint <c>POST /api/v1/almacen/devoluciones-proveedor/evidencia/blob</c>
 * (backend PR #260). FE sube el archivo, recibe la blobRef, luego
 * llama <c>useAdjuntarEvidenciaProveedor</c> con el blobRef + tipo +
 * nombre + comentario.
 */
export interface SubirEvidenciaResponse {
  blobRef: string;
  nombreArchivo: string;
  tamanoBytes: number;
  contentType: string;
}

/**
 * Hook para subir un archivo de evidencia (foto, email, video) al
 * blob storage de Almacén con permiso
 * <c>almacen.devoluciones-proveedor.iniciar</c>.
 */
export function useSubirEvidenciaBlob() {
  return useMutation({
    mutationFn: async (args: { archivo: File; idempotencyKey: string }) => {
      const formData = new FormData();
      formData.append('archivo', args.archivo);
      const { data } = await apiRequest<SubirEvidenciaResponse>(
        '/api/v1/almacen/devoluciones-proveedor/evidencia/blob',
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
