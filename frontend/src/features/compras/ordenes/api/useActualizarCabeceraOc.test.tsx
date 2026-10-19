import { describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import { useActualizarCabeceraOc } from '@/features/compras/ordenes/api/useActualizarCabeceraOc';

describe('useActualizarCabeceraOc', () => {
  it('PATCH a la cabecera con solo los campos presentes, Idempotency-Key e invalida el detalle', async () => {
    let method: string | null = null;
    let body: unknown = null;
    let idempotencyKey: string | null = null;
    mswServer.use(
      http.patch('*/api/v1/compras/ordenes/oc-1', async ({ request }) => {
        method = request.method;
        body = await request.json();
        idempotencyKey = request.headers.get('Idempotency-Key');
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidate = vi.spyOn(client, 'invalidateQueries');
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
    const { result } = renderHook(() => useActualizarCabeceraOc(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({
        ordenCompraId: 'oc-1',
        command: { proveedorId: 'prov-1', usoPrincipalId: 'uso-1' },
        idempotencyKey: 'key-123',
      });
    });

    expect(method).toBe('PATCH');
    expect(body).toEqual({ proveedorId: 'prov-1', usoPrincipalId: 'uso-1' });
    expect(idempotencyKey).toBe('key-123');
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ordenesKeys.detail('oc-1') });
  });
});
