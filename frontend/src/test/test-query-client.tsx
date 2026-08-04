import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { type ReactNode } from 'react';
import { ConflictDialogProvider } from '@/components/erp/collaboration/ConflictDialogProvider';
import { NuevaRequisicionContext } from '@/features/compras/components/nueva-requisicion-context';

/**
 * Helpers para tests de hooks que usan TanStack Query. Crea un
 * <c>QueryClient</c> nuevo por test (sin retries, sin cache compartido)
 * y envuelve el <c>renderHook</c> con <c>QueryClientProvider</c>.
 *
 * <para>Desde UF3-PR3 también incluye <c>&lt;ConflictDialogProvider/&gt;</c>
 * porque varias mutations del módulo Compras consumen
 * <c>useConflictDialog()</c> al manejar 409. Si no se incluye, esos
 * componentes lanzan al renderear. Los tests que NO usan el provider
 * ignoran el dialog "extra" que se renderea en el body — no afecta
 * sus assertions.</para>
 *
 * @example
 * ```ts
 * const { result } = renderHook(() => useRequisiciones(), {
 *   wrapper: createQueryWrapper(),
 * });
 * ```
 */
export function createTestQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        staleTime: 0,
        gcTime: 0,
      },
      mutations: {
        retry: false,
      },
    },
  });
}

/** No-op API del NuevaRequisicion modal para tests — los componentes
 * que consumen el hook reciben handlers que no hacen nada. Tests
 * que necesiten verificar el call site (que se invocó <c>abrir</c>)
 * pueden mockear el contexto manualmente. */
const NUEVA_REQUISICION_NOOP = Object.freeze({
  abrir: () => {},
  cerrar: () => {},
  setDirty: () => {},
});

export function createQueryWrapper(client?: QueryClient) {
  const queryClient = client ?? createTestQueryClient();
  return function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <ConflictDialogProvider>
          <NuevaRequisicionContext.Provider value={NUEVA_REQUISICION_NOOP}>
            {children}
          </NuevaRequisicionContext.Provider>
        </ConflictDialogProvider>
      </QueryClientProvider>
    );
  };
}
