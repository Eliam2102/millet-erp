import type { ReactNode } from 'react';
import { act, renderHook } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { expect, it, vi } from 'vitest';
import { useGuardarActivo } from './hooks';

const request = vi.hoisted(() => vi.fn());
vi.mock('@/lib/api', () => ({
  apiRequest: request,
  esConflictoConcurrencia: () => false,
  useBodyScopedIdempotencyKey: () => () => 'ensayo-multipart',
}));
vi.mock('@/components/erp/collaboration/conflict-dialog-context', () => ({
  useConflictDialog: () => ({ openSimple: vi.fn() }),
}));

it('conserva cuerpo y boundary sensible a mayúsculas al importar INPC', async () => {
  request.mockResolvedValue({ data: undefined });
  const client = new QueryClient();
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  const { result } = renderHook(() => useGuardarActivo(), { wrapper });
  const body = new Blob(['--WebKitCaseSensitive\r\n'], {
    type: 'multipart/form-data; boundary=WebKitCaseSensitive',
  });
  const headers = { 'Content-Type': 'multipart/form-data; boundary=WebKitCaseSensitive' };
  await act(() => result.current.mutateAsync({ ruta: 'inpc/excel', body, headers }));
  expect(body.type).toBe('multipart/form-data; boundary=webkitcasesensitive');
  expect(request).toHaveBeenCalledWith(
    '/api/v1/activos-fijos/inpc/excel',
    expect.objectContaining({ body, headers, idempotencyKey: 'ensayo-multipart' }),
  );
});
