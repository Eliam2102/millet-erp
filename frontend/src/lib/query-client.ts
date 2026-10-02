import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { esApiError } from '@/lib/api/error';

/**
 * Metadata opcional en <c>useMutation({ meta })</c> que reconoce el
 * backstop global. <c>toastOnError</c> hace que el <c>MutationCache</c>
 * muestre un toast genérico cuando la mutación falla — SOLO para hooks que
 * NO manejan el error en el <c>onError</c> del call-site (si lo hacen y
 * además activan esto, saldría doble toast).
 */
export interface MutationMetaErrorHandling {
  toastOnError?: boolean | string;
}

/**
 * Backstop de OBSERVABILIDAD: registra cualquier error de query/mutación
 * con su <c>traceId</c>, aunque el hook no defina <c>onError</c>. No
 * sustituye el manejo local; garantiza que ninguna falla quede sin rastro
 * (aunque el usuario no la reporte). Ver revisión FE 2026-07-18.
 */
function logClientError(scope: 'query' | 'mutation', error: unknown): void {
  // PLATFORM-TODO(<AppInsightsClient>): enviar a Application Insights además
  // de la consola (hoy solo queda en el log del navegador).
  if (esApiError(error)) {
    console.error(`[api] ${scope} error`, {
      status: error.status,
      code: error.code,
      traceId: error.traceId,
      title: error.problem.title,
      detail: error.problem.detail,
    });
  } else {
    console.error(`[api] ${scope} error`, error);
  }
}

/**
 * Aviso global de 403. El store de permisos solo se refresca en login o
 * cambio de empresa, así que si le quitan un permiso a mitad de sesión la UI
 * lo sigue mostrando hasta que la API lo rechaza. En lugar de refrescar la
 * sesión sola, el 403 se pinta como aviso. El <c>id</c> fijo colapsa varios
 * 403 simultáneos en un solo toast.
 */
function avisarSinPermiso(error: unknown): void {
  if (!esApiError(error) || error.status !== 403) return;
  toast.warning('Ya no tienes permiso para esta acción', {
    id: 'sin-permiso',
    description:
      error.problem.detail ??
      'Tus permisos pudieron cambiar. Vuelve a iniciar sesión para actualizarlos.',
  });
}

/**
 * Instancia única de QueryClient para toda la app. Configuración alineada
 * con ADR-0023:
 *
 * - **`staleTime: 30 s`** — datos frescos por 30 s después de cada fetch.
 * - **`gcTime: 5 min`** — cache se mantiene 5 min después de que ningún
 *   componente lo consume.
 * - **No reintentar errores 4xx** — son del cliente (mal request, falta
 *   permiso, etc.); reintentar no ayuda.
 * - **`refetchOnWindowFocus: true`** — al volver a la pestaña se recargan
 *   las queries con más de `staleTime`, para ver lo que cambiaron otros
 *   usuarios. SignalR (ADR-0001) hoy solo publica presencia, no
 *   invalidaciones de datos. Structural sharing evita re-renders si nada
 *   cambió; los formularios que se cargan desde una query deben usar
 *   `keepDirtyValues` para no pisar lo que el usuario está escribiendo.
 * - **Backstop global de errores** (`queryCache`/`mutationCache`): loguea
 *   toda falla; opcionalmente muestra toast genérico si la mutación lo pide
 *   por `meta.toastOnError` (opt-in, para no duplicar los toasts que ya
 *   emiten los `onError` del call-site).
 */
export const queryClient = new QueryClient({
  queryCache: new QueryCache({
    onError: (error) => {
      logClientError('query', error);
      avisarSinPermiso(error);
    },
  }),
  mutationCache: new MutationCache({
    onError: (error, _variables, _context, mutation) => {
      logClientError('mutation', error);
      const meta = mutation.meta as MutationMetaErrorHandling | undefined;
      if (!meta?.toastOnError) {
        avisarSinPermiso(error);
        return;
      }
      const fallback =
        typeof meta.toastOnError === 'string'
          ? meta.toastOnError
          : 'No se pudo completar la operación.';
      toast.error(esApiError(error) ? error.problem.title : fallback, {
        description: esApiError(error)
          ? (error.problem.detail ??
            (error.traceId ? `Código: ${error.traceId}` : undefined))
          : undefined,
      });
    },
  }),
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      gcTime: 5 * 60_000,
      refetchOnWindowFocus: true,
      retry: (failureCount, error) => {
        if (isClientError(error)) {
          return false;
        }
        return failureCount < 3;
      },
    },
  },
});

/** True si el error tiene una propiedad `status` numérica entre 400 y 499. */
function isClientError(error: unknown): boolean {
  if (typeof error !== 'object' || error === null) return false;
  const status = (error as { status?: unknown }).status;
  return typeof status === 'number' && status >= 400 && status < 500;
}
