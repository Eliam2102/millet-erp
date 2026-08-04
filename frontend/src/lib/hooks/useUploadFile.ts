import { useCallback, useRef, useState } from 'react';
import { apiBaseUrl } from '@/lib/auth/config';
import { useAuthStore } from '@/lib/auth/auth-store';
import { ApiError, type ProblemDetails } from '@/lib/api/error';

/**
 * <c>useUploadFile()</c> — hook genérico para upload de archivos
 * vía <c>multipart/form-data</c> con progress bar (XMLHttpRequest +
 * <c>onprogress</c>). Cross-módulo: usado por
 * <c>&lt;AdjuntosManager/&gt;</c> en OC; consumible por CxP/Activos/
 * Recepción cuando lleguen.
 *
 * <para>No usa <c>fetch</c> porque la API estándar no expone progreso
 * de upload (solo response stream). XHR sí, vía <c>xhr.upload.onprogress</c>.</para>
 *
 * <para><b>Auth + Idempotency-Key</b>: el hook inyecta automáticamente
 * el JWT del <c>useAuthStore</c> en <c>Authorization</c>; el caller
 * debe pasar <c>idempotencyKey</c> (UUID v4) si el endpoint lo requiere
 * (todos los POST/PUT del ERP, ADR-0020).</para>
 *
 * <para><b>Error handling</b>: si el server responde
 * <c>application/problem+json</c> con <c>!ok</c>, el hook lanza
 * <c>ApiError</c> tipado (mismo shape que <c>apiRequest</c>); otros
 * errores HTTP se envuelven en <c>ApiError</c> con
 * <c>code: 'UNKNOWN_HTTP_ERROR'</c>.</para>
 *
 * @example
 * ```tsx
 * const { upload, progress, isUploading, error } = useUploadFile();
 * await upload({
 *   url: `/api/v1/compras/ordenes/${ocId}/adjuntos`,
 *   file,
 *   extraFields: { tipoDocumentoId: tipoId },
 *   idempotencyKey: crypto.randomUUID(),
 * });
 * ```
 */

export interface UploadFileOptions {
  /** Path relativo (se prepende <c>apiBaseUrl</c>) o URL absoluta. */
  url: string;
  /** Archivo a subir. Se mapea al campo <c>archivo</c> del multipart. */
  file: File;
  /**
   * Nombre del campo del file en el multipart. Default <c>'archivo'</c>
   * para coincidir con el contrato del backend Compras (UF3-PR2).
   */
  fieldName?: string;
  /** Campos adicionales del multipart (key-value). Se serializan como string. */
  extraFields?: Record<string, string | Blob>;
  /**
   * Idempotency-Key (ADR-0020). Si <c>null</c>/<c>undefined</c> NO se
   * envía el header — el endpoint debe ser idempotente por shape (DELETE,
   * GET) o no requerirlo.
   */
  idempotencyKey?: string | null;
  /**
   * Headers extra (no incluyas <c>Content-Type</c> — el browser lo
   * setea con boundary automáticamente, ni <c>Authorization</c>).
   */
  extraHeaders?: Record<string, string>;
  /** AbortController.signal para cancelar el upload mid-stream. */
  signal?: AbortSignal;
}

export interface UploadFileState<TResponse> {
  /** Progreso 0-100 (0 antes de empezar, 100 al completar). */
  progress: number;
  isUploading: boolean;
  error: ApiError | null;
  data: TResponse | null;
}

export interface UploadFileApi<TResponse> extends UploadFileState<TResponse> {
  upload: (options: UploadFileOptions) => Promise<TResponse>;
  reset: () => void;
}

export function useUploadFile<TResponse = unknown>(): UploadFileApi<TResponse> {
  const [progress, setProgress] = useState(0);
  const [isUploading, setIsUploading] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [data, setData] = useState<TResponse | null>(null);
  const xhrRef = useRef<XMLHttpRequest | null>(null);

  const reset = useCallback(() => {
    setProgress(0);
    setIsUploading(false);
    setError(null);
    setData(null);
    xhrRef.current?.abort();
    xhrRef.current = null;
  }, []);

  const upload = useCallback(
    async (options: UploadFileOptions): Promise<TResponse> => {
      const {
        url,
        file,
        fieldName = 'archivo',
        extraFields,
        idempotencyKey,
        extraHeaders,
        signal,
      } = options;

      const fullUrl = url.startsWith('http') ? url : `${apiBaseUrl}${url}`;
      const token = useAuthStore.getState().accessToken;

      const formData = new FormData();
      formData.append(fieldName, file, file.name);
      if (extraFields) {
        for (const [key, value] of Object.entries(extraFields)) {
          formData.append(key, value);
        }
      }

      setProgress(0);
      setIsUploading(true);
      setError(null);
      setData(null);

      return new Promise<TResponse>((resolve, reject) => {
        const xhr = new XMLHttpRequest();
        xhrRef.current = xhr;

        xhr.open('POST', fullUrl, true);

        if (token) {
          xhr.setRequestHeader('Authorization', `Bearer ${token}`);
        }
        if (idempotencyKey) {
          xhr.setRequestHeader('Idempotency-Key', idempotencyKey);
        }
        if (extraHeaders) {
          for (const [k, v] of Object.entries(extraHeaders)) {
            xhr.setRequestHeader(k, v);
          }
        }

        xhr.upload.onprogress = (evt) => {
          if (evt.lengthComputable) {
            const pct = Math.round((evt.loaded / evt.total) * 100);
            setProgress(pct);
          }
        };

        xhr.onload = () => {
          setIsUploading(false);
          xhrRef.current = null;
          if (xhr.status === 401) {
            useAuthStore.getState().clearSession();
          }
          if (xhr.status >= 200 && xhr.status < 300) {
            setProgress(100);
            try {
              const parsed = (xhr.responseText
                ? JSON.parse(xhr.responseText)
                : null) as TResponse;
              setData(parsed);
              resolve(parsed);
            } catch {
              // Response no-JSON — devuelve null si el caller no usa el body.
              setData(null as unknown as TResponse);
              resolve(null as unknown as TResponse);
            }
            return;
          }
          // Error HTTP — intentar parsear ProblemDetails.
          let problem: ProblemDetails;
          try {
            const parsed = JSON.parse(xhr.responseText) as ProblemDetails;
            problem = parsed;
          } catch {
            problem = {
              type: 'about:blank',
              title: xhr.statusText || 'Error de red',
              status: xhr.status,
              detail: xhr.responseText || undefined,
              code: 'UNKNOWN_HTTP_ERROR',
            };
          }
          const apiError = new ApiError(problem, xhr.status);
          setError(apiError);
          reject(apiError);
        };

        xhr.onerror = () => {
          setIsUploading(false);
          xhrRef.current = null;
          const apiError = new ApiError(
            {
              type: 'about:blank',
              title: 'Network error',
              status: 0,
              code: 'NETWORK_ERROR',
            },
            0,
          );
          setError(apiError);
          reject(apiError);
        };

        xhr.onabort = () => {
          setIsUploading(false);
          xhrRef.current = null;
          const apiError = new ApiError(
            {
              type: 'about:blank',
              title: 'Upload cancelado',
              status: 0,
              code: 'UPLOAD_ABORTED',
            },
            0,
          );
          setError(apiError);
          reject(apiError);
        };

        if (signal) {
          if (signal.aborted) {
            xhr.abort();
            return;
          }
          signal.addEventListener('abort', () => xhr.abort());
        }

        xhr.send(formData);
      });
    },
    [],
  );

  return { upload, reset, progress, isUploading, error, data };
}
