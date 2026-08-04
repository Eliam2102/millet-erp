import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { useUploadFile } from '@/lib/hooks/useUploadFile';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';

/**
 * Response del backend tras adjuntar (UF3-PR2). Mirror de
 * <c>AdjuntarDocumentoResponse</c>.
 */
export interface AdjuntarDocumentoResponse {
  adjuntoId: string;
  /** URL del blob (storage). */
  blobUrl: string;
}

export interface AdjuntarDocumentoArgs {
  ordenCompraId: string;
  tipoDocumentoId: string;
  archivo: File;
  /** UUID v4 estable por intento — ADR-0020. Backend lo requiere. */
  idempotencyKey: string;
  signal?: AbortSignal;
}

/**
 * <c>useAdjuntarDocumento()</c> — wrapper sobre <c>useUploadFile</c>
 * que apunta al endpoint OC <c>POST /{id}/adjuntos</c>. Multipart con
 * <c>archivo</c> + <c>tipoDocumentoId</c>. Tras éxito invalida el
 * detalle de la OC para que el AdjuntosManager refleje la nueva fila.
 *
 * <para>Expone <c>progress</c> (0-100) para que el caller renderee una
 * barra de progreso por archivo. Para batches paralelos, montar varios
 * <c>useAdjuntarDocumento</c> (uno por archivo) o usar
 * <c>useUploadFile</c> directamente con coordinación manual.</para>
 */
export function useAdjuntarDocumento() {
  const queryClient = useQueryClient();
  const upload = useUploadFile<AdjuntarDocumentoResponse>();

  const adjuntar = useCallback(
    async (args: AdjuntarDocumentoArgs): Promise<AdjuntarDocumentoResponse> => {
      const result = await upload.upload({
        url: `/api/v1/compras/ordenes/${args.ordenCompraId}/adjuntos`,
        file: args.archivo,
        fieldName: 'archivo',
        extraFields: { tipoDocumentoId: args.tipoDocumentoId },
        idempotencyKey: args.idempotencyKey,
        signal: args.signal,
      });
      queryClient.invalidateQueries({
        queryKey: ordenesKeys.detail(args.ordenCompraId),
      });
      return result;
    },
    [queryClient, upload],
  );

  return {
    adjuntar,
    progress: upload.progress,
    isUploading: upload.isUploading,
    error: upload.error,
    data: upload.data,
    reset: upload.reset,
  };
}
