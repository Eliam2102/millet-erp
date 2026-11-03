import { expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { useEscaneoAccion, usePiezas } from './hooks';

const apiRequest = vi.hoisted(() => vi.fn());
vi.mock('@/lib/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api')>()),
  apiRequest,
}));
vi.mock('@/components/erp/collaboration/conflict-dialog-context', () => ({
  useConflictDialog: () => ({ openSimple: vi.fn() }),
}));

function wrapper(qc: QueryClient) {
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

it('useEscaneoAccion descarta con If-Match, Idempotency-Key y motivo, y refresca la lista', async () => {
  apiRequest.mockResolvedValue({ data: {} });
  const qc = new QueryClient();
  const spy = vi.spyOn(qc, 'invalidateQueries');
  const { result } = renderHook(() => useEscaneoAccion(), { wrapper: wrapper(qc) });
  await act(async () => {
    await result.current.mutateAsync({ escaneo: { id: 'e1', version: 3 }, ruta: 'descartar', body: { motivo: 'duplicada' } });
  });
  const [path, opts] = apiRequest.mock.calls[0];
  expect(path).toBe('/api/v1/producto-terminado/escaneos/e1/descartar');
  expect(opts).toMatchObject({ method: 'POST', body: { motivo: 'duplicada' }, ifMatch: '3' });
  expect(typeof opts.idempotencyKey).toBe('string');
  expect(spy).toHaveBeenCalledWith({ queryKey: ['producto-terminado'] });
});

it('usePiezas omite filtros vacíos en la query string', async () => {
  apiRequest.mockResolvedValue({ data: { items: [], total: 0, avance: [] } });
  const qc = new QueryClient();
  const { result } = renderHook(() => usePiezas({ pedido: 'P1', sucursalId: '', page: 1 }), { wrapper: wrapper(qc) });
  await vi.waitFor(() => expect(result.current.isSuccess).toBe(true));
  expect(apiRequest.mock.calls.at(-1)![0]).toBe('/api/v1/producto-terminado/piezas?pedido=P1&page=1');
});
