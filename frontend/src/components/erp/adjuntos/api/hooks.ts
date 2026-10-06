import {
  useMutation,
  useQuery,
  useQueryClient,
  type QueryClient,
} from '@tanstack/react-query';
import { useCallback } from 'react';
import { apiRequest, esApiError } from '@/lib/api';
import { apiBaseUrl } from '@/lib/auth/config';
import { useUploadFile } from '@/lib/hooks/useUploadFile';
import type {
  AdjuntoResponse,
  EnlaceDescarga,
  Expediente,
  SubirAdjuntoArgs,
  TipoDocumentoAdjunto,
} from './types';

/**
 * Capa de datos reutilizable del servicio genérico de adjuntos. Todos los
 * hooks reciben la "base de ruta" del padre (p. ej.
 * `/api/v1/datos-maestros/proveedores/{id}`); las rutas `/adjuntos...` y
 * `/expediente` cuelgan de ella (helper `MapAdjuntos` del backend).
 */

export const adjuntosKeys = {
  /** Prefijo por padre: invalida lista y expediente a la vez. */
  base: (base: string) => ['adjuntos', base] as const,
  lista: (base: string, incluirBajas: boolean) =>
    ['adjuntos', base, 'lista', incluirBajas] as const,
  expediente: (base: string) => ['adjuntos', base, 'expediente'] as const,
  tipos: (entidad: string) => ['adjuntos', 'tipos', entidad] as const,
};

function invalidar(qc: QueryClient, base: string) {
  return qc.invalidateQueries({ queryKey: adjuntosKeys.base(base) });
}

export function useAdjuntos(
  base: string,
  opciones: { incluirBajas?: boolean; enabled?: boolean } = {},
) {
  const incluirBajas = opciones.incluirBajas ?? false;
  return useQuery({
    queryKey: adjuntosKeys.lista(base, incluirBajas),
    enabled: opciones.enabled ?? true,
    queryFn: async ({ signal }) =>
      (
        await apiRequest<AdjuntoResponse[]>(
          `${base}/adjuntos${incluirBajas ? '?incluirBajas=true' : ''}`,
          { signal },
        )
      ).data,
  });
}

export function useExpediente(base: string, opciones: { enabled?: boolean } = {}) {
  return useQuery({
    queryKey: adjuntosKeys.expediente(base),
    enabled: opciones.enabled ?? true,
    queryFn: async ({ signal }) =>
      (await apiRequest<Expediente>(`${base}/expediente`, { signal })).data,
  });
}

export function useTiposDocumento(
  entidad: string,
  opciones: { enabled?: boolean } = {},
) {
  return useQuery({
    queryKey: adjuntosKeys.tipos(entidad),
    enabled: opciones.enabled ?? true,
    staleTime: 5 * 60_000, // catálogo casi estático
    queryFn: async ({ signal }) =>
      (
        await apiRequest<TipoDocumentoAdjunto[]>(
          `/api/v1/adjuntos/tipos?entidad=${encodeURIComponent(entidad)}`,
          { signal },
        )
      ).data,
  });
}

/** Subida multipart con progreso e Idempotency-Key; invalida lista y expediente. */
export function useSubirAdjunto(base: string) {
  const qc = useQueryClient();
  const up = useUploadFile<AdjuntoResponse>();

  const subir = useCallback(
    async (args: SubirAdjuntoArgs) => {
      const extraFields: Record<string, string> = {
        tipoDocumentoId: args.tipoDocumentoId,
      };
      if (args.vigenteHasta) extraFields.vigenteHasta = args.vigenteHasta;
      const r = await up.upload({
        url: `${base}/adjuntos`,
        file: args.archivo,
        fieldName: 'archivo',
        extraFields,
        idempotencyKey: args.idempotencyKey ?? crypto.randomUUID(),
        signal: args.signal,
      });
      await invalidar(qc, base);
      return r;
    },
    [base, qc, up],
  );

  return {
    subir,
    progress: up.progress,
    isUploading: up.isUploading,
    error: up.error,
    reset: up.reset,
  };
}

/** Baja lógica con motivo (5-500). La segunda baja responde 422 ADJUNTO_YA_DADO_DE_BAJA. */
export function useDarDeBajaAdjunto(base: string) {
  const qc = useQueryClient();
  return useMutation<
    AdjuntoResponse,
    Error,
    { adjuntoId: string; motivo: string }
  >({
    mutationFn: async ({ adjuntoId, motivo }) =>
      (
        await apiRequest<AdjuntoResponse>(`${base}/adjuntos/${adjuntoId}`, {
          method: 'DELETE',
          body: { motivo },
        })
      ).data,
    onSuccess: () => invalidar(qc, base),
  });
}

/** Dispara la descarga del enlace temporal (TTL 60 s) de inmediato. */
export function abrirEnlaceDescarga(url: string) {
  const a = document.createElement('a');
  a.href = url.startsWith('http') ? url : `${apiBaseUrl}${url}`;
  a.rel = 'noopener';
  document.body.appendChild(a);
  a.click();
  a.remove();
}

/**
 * Pide el enlace temporal y lo abre en el acto (no se cachea ni se guarda:
 * expira en 60 s). Devuelve el enlace por si el caller lo necesita.
 */
export function useEnlaceAdjunto(base: string) {
  return useMutation<EnlaceDescarga, Error, { adjuntoId: string }>({
    mutationFn: async ({ adjuntoId }) => {
      const { data } = await apiRequest<EnlaceDescarga>(
        `${base}/adjuntos/${adjuntoId}/enlace`,
        { method: 'POST' },
      );
      abrirEnlaceDescarga(data.url);
      return data;
    },
  });
}

/** Mensaje claro para el usuario según el estatus del Problem Details. */
export function mensajeErrorAdjunto(err: unknown): string {
  if (!esApiError(err)) {
    return err instanceof Error ? err.message : 'Ocurrió un error inesperado.';
  }
  const detalle = err.problem.detail ?? err.problem.title;
  switch (err.status) {
    case 401:
      return 'Tu sesión expiró o el enlace ya no es válido. Vuelve a iniciar sesión e intenta de nuevo.';
    case 403:
      return 'No tienes permiso para realizar esta acción sobre los documentos.';
    case 404:
      return 'El documento o el registro ya no existe (o fue dado de baja).';
    case 422:
      return err.code === 'ADJUNTO_YA_DADO_DE_BAJA'
        ? 'Este documento ya fue dado de baja.'
        : detalle;
    case 400: {
      const campo =
        err.problem.errores?.[0]?.mensaje ??
        Object.values(err.problem.errors ?? {})[0]?.[0];
      return campo ?? detalle ?? 'Revisa los datos capturados.';
    }
    default:
      return err.status >= 500
        ? 'El servidor no pudo completar la operación. Intenta de nuevo en unos minutos.'
        : detalle;
  }
}
