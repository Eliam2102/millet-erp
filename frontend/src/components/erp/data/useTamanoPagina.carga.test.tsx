import { afterEach, expect, it, vi } from 'vitest';
import { act, cleanup, render, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useTamanoPagina } from './useTamanoPagina';
import { TableSkeleton } from '@/components/erp/feedback/TableSkeleton';
import { useExpedientes } from '@/features/comercio-exterior/api/hooks';
import { useAuthStore } from '@/lib/auth/auth-store';

const state = vi.hoisted(() => ({
  requests: [] as number[], canceled: [] as number[], completed: [] as number[],
}));
vi.mock('@/lib/api', () => ({
  esConflictoConcurrencia: () => false,
  useBodyScopedIdempotencyKey: () => () => 'ficticio',
  apiRequest: vi.fn((path: string, { signal }: { signal: AbortSignal }) => {
    const n = Number(new URL(path, 'http://local.invalid').searchParams.get('limit'));
    state.requests.push(n);
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        state.completed.push(n);
        resolve({ data: {
          items: Array.from({ length: n }, (_, i) => ({ id: String(i), folio: `MUESTRA-${i}` })),
          total: 40, offset: 0, limit: n,
        } });
      }, n === 5 ? 1200 : 15);
      signal.addEventListener('abort', () => {
        state.canceled.push(n);
        clearTimeout(timer);
        reject(new DOMException('Cancelado', 'AbortError'));
      });
    });
  }),
}));
const limits: number[] = [];
function Caso() {
  const { limit } = useTamanoPagina();
  const query = useExpedientes({ limit });
  useEffect(() => { limits.push(limit); }, [limit]);
  return <div data-table-viewport>
    {query.isLoading ? <TableSkeleton /> : <table>
      <thead><tr><th>Expediente</th></tr></thead>
      <tbody>{query.data?.items.map((r) => <tr key={r.id}><td>{r.folio}</td></tr>)}</tbody>
    </table>}
  </div>;
}
afterEach(() => { cleanup(); vi.restoreAllMocks(); localStorage.clear(); });

it('no cancela consultas en bucle al medir el skeleton entre datos cacheados y la página nueva', async () => {
  state.requests.length = 0; state.canceled.length = 0; state.completed.length = 0; limits.length = 0;
  localStorage.clear(); window.history.replaceState({}, '', '/comercio-exterior/expedientes');
  useAuthStore.setState({
    status: 'authenticated', accessToken: 'FICTICIO', currentEmpresaId: 'e',
    user: { id: 'audit', nombre: 'Ficticio', email: 'f@example.invalid' },
  });
  // Limita la reproducción defectuosa; el presupuesto de consultas aún falla antes de la corrección.
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockImplementation(function (this: HTMLElement) {
    return this.hasAttribute('data-table-viewport') && limits.length < 14 ? 432 : 0;
  });
  vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    const height = this.tagName === 'TR' ? this.closest('thead') ? 36 : 80 : 0;
    return { top: 0, left: 0, right: 0, bottom: height, width: 0, height, x: 0, y: 0, toJSON: () => ({}) };
  });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 30_000, gcTime: 300_000 } } });
  client.setQueryData(['comercio-exterior', 'lista', { limit: 9 }], {
    items: Array.from({ length: 9 }, (_, i) => ({ id: String(i), folio: `MUESTRA-${i}` })),
    total: 40, offset: 0, limit: 9,
  });
  const view = render(<QueryClientProvider client={client}><Caso /></QueryClientProvider>);
  await waitFor(() => expect(state.completed).toContain(5), { timeout: 3000 });
  const completedCount = state.requests.length;
  await act(async () => { await new Promise((resolve) => setTimeout(resolve, 100)); });
  expect(state.requests.length).toBeLessThanOrEqual(3);
  expect(state.requests.length).toBe(completedCount);
  expect(limits.at(-1)).toBe(5);
  expect(state.requests).toEqual([10, 5]);
  expect(state.completed).toEqual([10, 5]);
  expect(state.canceled).toEqual([]);
  view.unmount(); client.clear();
});
